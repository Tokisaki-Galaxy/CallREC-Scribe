// Services/MediaConversionService.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

#if WINDOWS
using FFMpegCore;
#elif ANDROID
using Ffmpegkit;
using Ffmpegkit.Droid;
#endif

namespace CallREC_Scribe.Services
{
    public class MediaConversionService
    {
#if ANDROID
        /// <summary>
        /// 实现了 FFmpegKit 回调接口的内部类。
        /// 它持有一个 TaskCompletionSource，用于在原生代码完成时通知 .NET 异步任务。
        /// </summary>
        private class FFmpegSessionCompleteCallback : Java.Lang.Object, IFFmpegSessionCompleteCallback
        {
            private readonly TaskCompletionSource<FFmpegSession> _tcs;

            public FFmpegSessionCompleteCallback(TaskCompletionSource<FFmpegSession> tcs)
            {
                _tcs = tcs;
            }

            /// <summary>
            /// 当 FFmpeg 原生任务完成时，此方法被调用。
            /// </summary>
            /// <param name="session">包含执行结果的会话对象。</param>
            public void Apply(FFmpegSession session)
            {
                Debug.WriteLine($"[FFmpegSessionCompleteCallback] 回调已触发，返回码: {session.ReturnCode}");
                // 使用 TrySetResult 来确保即使回调被意外调用多次也不会抛出异常，从而完成 await tcs.Task 的等待。
                _tcs.TrySetResult(session);
            }
        }
#endif
        /// <summary>
        /// 运行一个独立的 FFmpeg-kit 测试，以验证回调机制是否正常工作。
        /// 这个测试只获取 FFmpeg 版本，不涉及文件 I/O。
        /// </summary>
        /// <returns>如果 FFmpeg 成功执行并返回，则为 true；否则为 false。</returns>
        public async Task<bool> RunFfmpegCallbackTestAsync()
        {
            Debug.WriteLine("[FFmpegTest] 开始进行回调机制测试...");
#if ANDROID
            // 1. 使用 TaskCompletionSource 来桥接回调和异步任务
            var tcs = new TaskCompletionSource<FFmpegSession>();
            // 2. 创建回调实例。这是最关键的一步，必须确保此对象在回调发生前不被GC回收。
            var completeCallback = new FFmpegSessionCompleteCallback(tcs);
            try
            {
                string command = "-version";
                Debug.WriteLine($"[FFmpegTest] 准备执行命令: '{command}'");

                FFmpegKit.ExecuteAsync(command, completeCallback);
                Debug.WriteLine("[FFmpegTest] 命令已提交，正在异步等待回调...");
                var completedSession = await tcs.Task;
                GC.KeepAlive(completeCallback);
                Debug.WriteLine("[FFmpegTest] 回调成功返回！任务完成。");
                if (ReturnCode.IsSuccess(completedSession.ReturnCode))
                {
                    Debug.WriteLine($"[FFmpegTest] FFmpeg 成功执行。版本信息: {completedSession.Output}");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"[FFmpegTest] FFmpeg 执行失败，返回码: {completedSession.ReturnCode}.");
                    Debug.WriteLine($"[FFmpegTest] 日志: {completedSession.GetAllLogsAsString}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // 确保在异常情况下对象也能存活到 catch 块结束
                GC.KeepAlive(completeCallback);
                Debug.WriteLine($"[FFmpegTest] 测试执行期间发生异常: {ex}");
                return false;
            }
#else
    // 在其他平台上，此测试不适用
    Debug.WriteLine("[FFmpegTest] 测试仅适用于 Android 平台。");
    await Task.CompletedTask; // 保持方法签名一致
    return false;
#endif
        }

        private async Task<string> CreateLocalCopyOfFileAsync(string originalFilePath)
        {
            try
            {
                // 为副本创建一个唯一的文件名，以避免冲突
                string safeOriginalFileName = Path.GetFileName(originalFilePath).Replace(' ', '_');
                string tempFileName = $"{Guid.NewGuid()}_{safeOriginalFileName}";
                string tempFilePath = Path.Combine(FileSystem.CacheDirectory, tempFileName);

                // 使用 .NET 的标准文件 API 读取原始文件并写入到缓存目录
                using (var sourceStream = File.OpenRead(originalFilePath))
                using (var destinationStream = File.Create(tempFilePath))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                Debug.WriteLine($"[MediaConversionService] 已成功创建文件副本: {tempFilePath}");
                return tempFilePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaConversionService] 创建文件副本失败: {ex.Message}");
                return null;
            }
        }

        public async Task<string> PrepareAudioForTranscriptionAsync(string originalFilePath, string engineModelType)
        {
            int targetSampleRate;
            // 根据引擎模型确定目标采样率
            if (engineModelType.StartsWith("8k", StringComparison.OrdinalIgnoreCase))
                targetSampleRate = 8000;
            else
                targetSampleRate = 16000;

            try
            {
                // 创建一个唯一的输出文件名，并将其放在应用的缓存目录中
                string safeFileNameWithoutExt = Path.GetFileNameWithoutExtension(originalFilePath).Replace(' ', '_');
                string outputFileName = $"{safeFileNameWithoutExt}_{Guid.NewGuid()}.mp3";
                string outputFilePath = Path.Combine(FileSystem.CacheDirectory, outputFileName);

#if WINDOWS
                // Windows 平台的实现 (使用 FFMpegCore)
                return await Task.Run(async () =>
                {
                    await FFMpegArguments
                        .FromFileInput(originalFilePath)
                        .OutputToFile(outputFilePath, true, options => options
                            .WithAudioSamplingRate(targetSampleRate) // 设置采样率
                            .ForceFormat("mp3")                     // 强制输出为 mp3
                            .WithAudioCodec("libmp3lame")           // 使用 libmp3lame 编码器
                        )
                        .ProcessAsynchronously();
                
                    return outputFilePath;
                });

#elif ANDROID
                // Android 平台的实现 (使用 FFmpegKit.Android)
                var localFileCopyPath = await CreateLocalCopyOfFileAsync(originalFilePath);
                if (string.IsNullOrEmpty(localFileCopyPath))
                {
                    Debug.WriteLine("[MediaConversionService] 无法创建文件的本地副本，转换中止。");
                    return null;
                }

                return await Task.Run(async () =>
                {
                    var tcs = new TaskCompletionSource<FFmpegSession>();

                    // 1. 实例化回调对象。这个对象需要保持存活，直到原生任务完成。
                    var completeCallback = new FFmpegSessionCompleteCallback(tcs);

                    try
                    {
                        string command = $"-y -i \"{localFileCopyPath}\" -ar {targetSampleRate} -ac 1 -c:a libmp3lame \"{outputFilePath}\"";

                        Debug.WriteLine($"[MediaConversionService] 当你能看到这行字的时候，很有可能会执行失败，卡在转圈圈上面。因为调试器阻止回调，这是我历时12h+发现的...但是不用调试器直接运行的时候一切正常");
                        Debug.WriteLine($"[MediaConversionService] FFmpeg即将启动 (在后台线程上),命令{command}");
                        FFmpegKit.ExecuteAsync(command, completeCallback);

                        GC.KeepAlive(completeCallback);
                        var completedSession = await tcs.Task;
                        Debug.WriteLine($"[MediaConversionService] FFmpeg任务已结束");

                        if (ReturnCode.IsSuccess(completedSession.ReturnCode))
                        {
                            Debug.WriteLine($"[MediaConversionService] FFmpeg成功完成，输出文件: {outputFilePath}");
                            File.Delete(localFileCopyPath);
                            return outputFilePath;
                        }
                        else
                        {
                            // 如果失败，打印详细日志以便调试。
                            Debug.WriteLine($"[MediaConversionService] FFmpegKit 执行失败，返回码: {completedSession.ReturnCode}.");
                            var allLogs =  completedSession.GetAllLogsAsString(30);
                            Debug.WriteLine($"[MediaConversionService] FFmpeg Logs: {allLogs}");
                            File.Delete(localFileCopyPath);
                            return null;
                        }
                    }
                    catch (Exception ex)
                    {
                        // 确保在异常情况下也能保持对象存活，直到 catch 块结束。
                        GC.KeepAlive(completeCallback);
                        Debug.WriteLine($"[MediaConversionService] FFmpeg 执行期间发生异常: {ex.Message}");
                        return null;
                    }
                });

#else
                // 其他平台的备用实现
                Debug.WriteLine($"[MediaConversionService] 媒体转换功能未在当前平台实现。");
                return await Task.FromResult<string>(null);
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaConversionService] 转换过程中发生外部异常: {ex.Message}");
                return await Task.FromResult<string>(null);
            }
        }
    }
}