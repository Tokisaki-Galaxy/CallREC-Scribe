using DocumentFormat.OpenXml.Vml;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;
using static CallREC_Scribe.Services.TencentCloudApiSigner;

namespace CallREC_Scribe.Services
{
    public class TencentAsrService
    {
        private readonly MediaConversionService _mediaConversionService;

        // --- 可配置参数 ---
        private const string Region = "ap-shanghai";    // 没啥用

        // Base64 编码后的大小限制 (5MB)。API 的限制是针对编码后的数据。
        private const long Base64SizeLimit = 5 * 1024 * 1024;

        // Base64 编码会使文件增大约33%。所以原始文件大小限制应为 5MB / 1.33 ≈ 3.75MB。
        // 我们设置一个更安全的值，例如 3.5MB，以避免临界问题。
        private const long RawFileSizeLimit = (long)(3.5 * 1024 * 1024);

        public TencentAsrService(MediaConversionService mediaConversionService)
        {
            _mediaConversionService = mediaConversionService;
        }

        public async System.Threading.Tasks.Task<string> TranscribeAsync(string filePath, string secretId, string secretKey, Action<string> onProgress)
        {
            HttpClient httpClient = new HttpClient();
            if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(secretKey))
            {
                return "错误：API密钥未配置。";
            }
            if (!File.Exists(filePath))
            {
                return "错误：本地文件不存在。";
            }

            const string Region = "ap-guangzhou";
            const string Service = "asr";
            const string ApiVersion = "2019-06-14";
            const string Endpoint = "https://asr.tencentcloudapi.com";

            const long Base64SizeLimit = 5 * 1024 * 1024;
            const long RawFileSizeLimit = 5 * 1024 * 1024;

            string fileToProcessPath = null;
            try
            {
                Debug.WriteLine($"[TencentAsrService] 文件转换/重采样");
                onProgress?.Invoke("文件转换/重采样...");
                var engineModelType = Preferences.Get("TencentEngineModel", "8k_zh");
                // if (Debugger.IsAttached) { fileToProcessPath = filePath; } else {
                    fileToProcessPath = await _mediaConversionService.PrepareAudioForTranscriptionAsync(filePath, engineModelType);
                // }
                if (string.IsNullOrEmpty(fileToProcessPath)) return "错误：音频文件格式转换失败，无法进行转录。";

                Debug.WriteLine($"[TencentAsrService] 文件转换/重采样完成, 准备上传...");
                onProgress?.Invoke("文件转换/重采样完成, 准备上传...");

                var fileInfo = new FileInfo(fileToProcessPath);
                if (fileInfo.Length > RawFileSizeLimit)
                {
                    return $"错误：文件大小超过 {RawFileSizeLimit / 1024 / 1024:F2} MB，无法直接上传。";
                }
                if (fileInfo.Length == 0) return "错误：文件大小为0。";

                onProgress?.Invoke("文件编码中...");
                byte[] fileBytes = await File.ReadAllBytesAsync(fileToProcessPath).ConfigureAwait(false);
                string base64Data = Convert.ToBase64String(fileBytes);

                if (Encoding.UTF8.GetByteCount(base64Data) > Base64SizeLimit)
                {
                    return $"错误：文件Base64编码后超过5MB，无法上传。";
                }

                // ---------- 1. 创建录音识别任务 (CreateRecTask) ----------
                onProgress?.Invoke("编码完成，正在创建识别任务...");
                Debug.WriteLine($"[TencentAsrService] 正在创建识别任务 (CreateRecTask)");

                long taskId;
                var createRecTaskBody = new
                {
                    EngineModelType = engineModelType,
                    ChannelNum = 1,
                    ResTextFormat = 0,
                    SourceType = 1,
                    Data = base64Data,
                    DataLen = (ulong)fileBytes.Length
                };
                // 使用 Newtonsoft.Json 进行序列化
                string jsonPayload = JsonConvert.SerializeObject(createRecTaskBody);

                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                    {
                        Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                    };

                    await TencentCloudApiSigner.AddApiSignatureHeadersAsync(httpClient, request, secretId, secretKey, Service, Region, "CreateRecTask", ApiVersion);

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        return $"错误：创建任务API请求失败。状态码: {response.StatusCode}, 响应: {responseBody}";
                    }

                    // 使用 Newtonsoft.Json (JObject) 进行解析
                    var jsonResponse = JObject.Parse(responseBody);
                    var responseToken = jsonResponse["Response"];

                    if (responseToken?["Data"]?["TaskId"] != null)
                    {
                        taskId = responseToken["Data"]["TaskId"].Value<long>();
                    }
                    else
                    {
                        string errorMsg = responseToken?["Error"]?["Message"]?.ToString() ?? "未知的API响应格式";
                        return $"错误：创建任务失败。{errorMsg}";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TencentAsrService] 创建任务时发生异常: {ex}");
                    return $"创建任务时发生异常: {ex.Message}";
                }

                Debug.WriteLine($"[TencentAsrService] 成功创建任务, TaskID: {taskId}. 开始轮询状态...");
                onProgress?.Invoke("已上传，等待云端处理...");

                // ---------- 2. 轮询任务状态 (DescribeTaskStatus) ----------
                while (true)
                {
                    await System.Threading.Tasks.Task.Delay(3000).ConfigureAwait(false);
                    Debug.WriteLine($"[TencentAsrService] 查询任务状态, TaskID: {taskId}");

                    var describeTaskBody = new { TaskId = taskId };
                    jsonPayload = JsonConvert.SerializeObject(describeTaskBody);

                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                        {
                            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                        };

                        await TencentCloudApiSigner.AddApiSignatureHeadersAsync(httpClient, request, secretId, secretKey, Service, Region, "DescribeTaskStatus", ApiVersion);

                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        HttpResponseMessage response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[TencentAsrService] 查询状态API请求失败。状态码: {response.StatusCode}, 响应: {responseBody}");
                            onProgress?.Invoke("查询状态失败，正在重试...");
                            continue;
                        }

                        var jsonResponse = JObject.Parse(responseBody);
                        var data = jsonResponse?["Response"]?["Data"];

                        if (data == null)
                        {
                            Debug.WriteLine($"[TencentAsrService] 查询状态响应格式不正确: {responseBody}");
                            onProgress?.Invoke("查询状态响应无效，正在重试...");
                            continue;
                        }

                        int status = data["Status"].Value<int>();
                        string statusStr = data["StatusStr"].Value<string>();
                        Debug.WriteLine($"[TencentAsrService] 任务状态: {status} ({statusStr})");

                        switch (status)
                        {
                            case 2: // 任务成功
                                return data["Result"]?.ToString() ?? "任务成功，但未返回结果。";
                            case -1: // 任务失败
                                string errorMsg = data["ErrorMsg"]?.ToString() ?? "未知错误";
                                return $"转录失败：{errorMsg}";
                            case 0: // 任务排队中
                            case 1: // 任务执行中
                                onProgress?.Invoke($"云端处理中 ({statusStr})...");
                                break;
                            default:
                                Debug.WriteLine($"[TencentAsrService] 未知的任务状态: {status}");
                                return "错误：未知的任务状态。";
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        Debug.WriteLine("[TencentAsrService] 查询任务状态超时 (5秒)，将继续下一次轮询...");
                        onProgress?.Invoke("云端处理超时，正在重试...");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TencentAsrService] 查询状态时发生异常: {ex}");
                        onProgress?.Invoke("查询状态时出错，正在重试...");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TencentAsrService] 发生未知异常: {ex}");
                return $"发生未知异常：{ex.Message}\n详细信息请查看调试输出。";
            }
            finally
            {
                 if (!string.IsNullOrEmpty(fileToProcessPath) && File.Exists(fileToProcessPath))
                {
                    try
                    {
                        File.Delete(fileToProcessPath);
                        Debug.WriteLine($"[TencentAsrService] 已清理临时文件: {fileToProcessPath}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TencentAsrService] 清理临时文件失败: {ex.Message}");
                    }
                }
            }
        }
    }
}