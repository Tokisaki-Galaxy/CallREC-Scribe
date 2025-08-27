using DocumentFormat.OpenXml.Vml;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TencentCloud.Asr.V20190614;
using TencentCloud.Asr.V20190614.Models;
using TencentCloud.Common;
using TencentCloud.Common.Profile;

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
            if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(secretKey))
            {
                return "错误：API密钥未配置。";
            }
            if (!File.Exists(filePath))
            {
                return "错误：本地文件不存在。";
            }

            try
            {
                // 检查并转换文件格式（如果需要）
                Debug.WriteLine($"[TencentAsrService] 文件转换/重采样");
                onProgress?.Invoke("文件转换/重采样...");
                var engineModelType = Preferences.Get("TencentEngineModel", "8k_zh"); // 默认值8k
                string fileToProcessPath = await _mediaConversionService.PrepareAudioForTranscriptionAsync(filePath, engineModelType);
                if (string.IsNullOrEmpty(fileToProcessPath)) return "错误：音频文件格式转换失败，无法进行转录。";
                Debug.WriteLine($"[TencentAsrService] 文件转换/重采样完成, 准备上传...");
                onProgress?.Invoke("文件转换/重采样完成, 准备上传...");
                try
                {
                    var fileInfo = new FileInfo(fileToProcessPath);
                    if (fileInfo.Length > RawFileSizeLimit)
                    {
                        return $"错误：文件大小超过 {RawFileSizeLimit / 1024 / 1024:F2} MB，无法直接上传。";
                    }
                    if (fileInfo.Length == 0) return "错误：文件大小为0。";

                    // 读取文件并进行 Base64 编码
                    onProgress?.Invoke("文件编码中...");
                    byte[] fileBytes = await File.ReadAllBytesAsync(fileToProcessPath).ConfigureAwait(false);
                    string base64Data = Convert.ToBase64String(fileBytes);

                    // 再次确认编码后的大小，以防万一
                    if (System.Text.Encoding.UTF8.GetByteCount(base64Data) > Base64SizeLimit)
                    {
                        return $"错误：文件Base64编码后超过5MB，无法上传。";
                    }

                    // 初始化 SDK 客户端
                    onProgress?.Invoke("编码完成，初始化SDK客户端...");
                    Debug.WriteLine($"[TencentAsrService] 初始化 SDK 客户端");
                    Credential cred = new Credential { SecretId = secretId, SecretKey = secretKey };
                    ClientProfile clientProfile = new ClientProfile();
                    HttpProfile httpProfile = new HttpProfile { Endpoint = "asr.tencentcloudapi.com" };
                    clientProfile.HttpProfile = httpProfile;
                    AsrClient client = new AsrClient(cred, Region, clientProfile);

                    // 创建录音文件识别请求 (CreateRecTask)
                    CreateRecTaskRequest req = new CreateRecTaskRequest
                    {
                        EngineModelType = engineModelType,
                        ChannelNum = 1, // 单声道
                        ResTextFormat = 0, // 识别结果文本格式：0 表示带时间戳的句子级输出
                        SourceType = 1,
                        Data = base64Data,
                        DataLen = (ulong)fileBytes.Length
                    };

                    // 发送请求，获取任务ID
                    CreateRecTaskResponse resp = await client.CreateRecTask(req).ConfigureAwait(false);
                    ulong? taskId = resp.Data?.TaskId;

                    if (taskId == null)
                    {
                        return "错误：创建识别任务失败，未能获取任务ID。";
                    }
                    Debug.WriteLine($"[TencentAsrService] 成功创建任务, TaskID: {taskId}. 开始轮询状态...");
                    onProgress?.Invoke("已上传，等待云端处理...");
                    await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);
                    while (true)
                    {
                        await System.Threading.Tasks.Task.Delay(3000).ConfigureAwait(false);

                        Debug.WriteLine($"[TencentAsrService] 等待查询");
                        DescribeTaskStatusRequest statusReq = new DescribeTaskStatusRequest { TaskId = taskId };

                        var statusTask = client.DescribeTaskStatus(statusReq);
                        var timeoutTask = System.Threading.Tasks.Task.Delay(3000);

                        var completedTask = await System.Threading.Tasks.Task.WhenAny(statusTask, timeoutTask).ConfigureAwait(false);

                        if (completedTask == timeoutTask)
                        {
                            // 如果是超时任务先完成了
                            Debug.WriteLine("[TencentAsrService] 查询任务状态超时 (3秒)，将继续下一次轮询...");
                            onProgress?.Invoke("云端处理超时，正在重试...");
                            continue; // 跳过本次循环，进入下一次轮询
                        }

                        // 如果是状态查询任务先完成了
                        // 现在可以安全地获取结果，因为它已经完成了
                        DescribeTaskStatusResponse statusResp = await statusTask.ConfigureAwait(false);
                        Debug.WriteLine($"[TencentAsrService] 任务状态: {statusResp.Data?.Status}, 错误信息: {statusResp.Data?.ErrorMsg}");
                        // 0:任务排队中, 1:任务执行中, 2:任务成功, -1:任务失败
                        switch (statusResp.Data?.Status)
                        {
                            case 2: // 任务成功
                                return statusResp.Data.Result ?? "任务成功，但未返回结果。";
                            case -1: // 任务失败
                                return $"转录失败：{statusResp.Data.ErrorMsg}";
                            case 0: // 任务排队中
                            case 1: // 任务执行中，继续等待
                                break;
                            default:
                                return "错误：未知的任务状态。";
                        }
                    }
                }
                finally
                {
                    // 清理操作：删除处理过的临时文件
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
            catch (TencentCloudSDKException tcEx)
            {
                // 捕获并格式化腾讯云SDK的特定异常信息
                string errorDetails;
                if (string.IsNullOrEmpty(tcEx.ErrorCode) && tcEx.InnerException != null)
                {
                    // 如果ErrorCode为空，说明问题可能出在网络层或更底层，打印内部异常信息
                    errorDetails = $"SDK内部错误: \n" +
                                   $"类型: {tcEx.InnerException.GetType().Name}\n" +
                                   $"信息: {tcEx.InnerException.Message}";
                    Debug.WriteLine($"[TencentAsrService] SDK内部异常: {tcEx.InnerException}");
                }
                else
                {
                    errorDetails = $"API请求失败: \n" +
                                   $"错误码: {tcEx.ErrorCode}\n" +
                                   $"信息: {tcEx.Message}\n" +
                                   $"请求ID: {tcEx.RequestId}";
                }

                Debug.WriteLine($"[TencentAsrService] {errorDetails}");
                return errorDetails;
            }
            catch (Exception ex)
            {
                // 捕获其他通用异常 (如网络、IO等)，并提供完整的堆栈跟踪
                Debug.WriteLine($"[TencentAsrService] 发生未知异常: {ex}");
                return $"发生未知异常：{ex.Message}\n详细信息请查看调试输出。";
            }
        }
    }
}