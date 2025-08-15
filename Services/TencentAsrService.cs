using System;
using System.IO;
using System.Threading.Tasks;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Asr.V20190614;
using TencentCloud.Asr.V20190614.Models;

namespace CallREC_Scribe.Services
{
    public class TencentAsrService
    {
        // --- 可配置参数 ---
        private const string Region = "ap-shanghai";    // 没啥用

        // 识别引擎类型
        private const string EngineModelType = "8k_zh";

        // Base64 编码后的大小限制 (5MB)。API 的限制是针对编码后的数据。
        private const long Base64SizeLimit = 5 * 1024 * 1024;

        // Base64 编码会使文件增大约33%。所以原始文件大小限制应为 5MB / 1.33 ≈ 3.75MB。
        // 我们设置一个更安全的值，例如 3.5MB，以避免临界问题。
        private const long RawFileSizeLimit = (long)(3.5 * 1024 * 1024);


        public async System.Threading.Tasks.Task<string> TranscribeAsync(string filePath, string secretId, string secretKey)
        {
            // 1. 输入验证
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
                // 2. 检查文件大小
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > RawFileSizeLimit)
                {
                    return $"错误：文件大小超过 {RawFileSizeLimit / 1024 / 1024:F2} MB，无法直接上传。请使用其他方法处理大文件。";
                }
                if (fileInfo.Length == 0)
                {
                    return "错误：文件大小为0。";
                }

                // 3. 读取文件并进行 Base64 编码
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                string base64Data = Convert.ToBase64String(fileBytes);

                // 再次确认编码后的大小，以防万一
                if (System.Text.Encoding.UTF8.GetByteCount(base64Data) > Base64SizeLimit)
                {
                    return $"错误：文件Base64编码后超过5MB，无法上传。";
                }

                // 4. 初始化 SDK 客户端
                Credential cred = new Credential { SecretId = secretId, SecretKey = secretKey };
                ClientProfile clientProfile = new ClientProfile();
                HttpProfile httpProfile = new HttpProfile { Endpoint = "asr.tencentcloudapi.com" };
                clientProfile.HttpProfile = httpProfile;
                AsrClient client = new AsrClient(cred, Region, clientProfile);

                // 5. 创建录音文件识别请求 (CreateRecTask)
                CreateRecTaskRequest req = new CreateRecTaskRequest
                {
                    EngineModelType = EngineModelType,
                    ChannelNum = 1, // 通话录音通常是单声道
                    ResTextFormat = 0, // 识别结果文本格式：0 表示带时间戳的句子级输出
                    SourceType = 1,
                    Data = base64Data,
                    DataLen = (ulong)fileBytes.Length
                };

                // 6. 发送请求，获取任务ID
                CreateRecTaskResponse resp = await client.CreateRecTask(req);
                ulong? taskId = resp.Data?.TaskId;

                if (taskId == null)
                {
                    return "错误：创建识别任务失败，未能获取任务ID。";
                }

                // 7. 轮询任务状态 (DescribeTaskStatus)
                while (true)
                {
                    // 每隔3秒查询一次状态
                    await System.Threading.Tasks.Task.Delay(3000);

                    DescribeTaskStatusRequest statusReq = new DescribeTaskStatusRequest { TaskId = taskId };
                    DescribeTaskStatusResponse statusResp = await client.DescribeTaskStatus(statusReq);

                    // 0:任务排队中, 1:任务执行中, 2:任务成功, -1:任务失败
                    switch (statusResp.Data?.Status)
                    {
                        case 2: // 任务成功
                            return statusResp.Data.Result ?? "任务成功，但未返回结果。";
                        case -1: // 任务失败
                            return $"转录失败：{statusResp.Data.ErrorMsg}";
                        case 0: // 任务排队中
                        case 1: // 任务执行中
                            // 继续等待
                            break;
                        default:
                            return "错误：未知的任务状态。";
                    }
                }
            }
            catch (Exception ex)
            {
                // 捕获SDK或IO异常
                return $"发生异常：{ex.Message}";
            }
        }
    }
}