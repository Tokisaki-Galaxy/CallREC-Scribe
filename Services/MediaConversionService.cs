// Services/MediaConversionService.cs
using System;
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
        // --- 1. 创建一个辅助类来实现 Android 的回调接口 ---
#if ANDROID
        private class FFmpegSessionCompleteCallback : Java.Lang.Object, IFFmpegSessionCompleteCallback
        {
            private readonly Action<FFmpegSession> _onComplete;

            public FFmpegSessionCompleteCallback(Action<FFmpegSession> onComplete)
            {
                _onComplete = onComplete;
            }

            // 当 FFmpegKit 完成时，这个方法会被原生代码调用
            public void Apply(FFmpegSession session)
            {
                // 然后我们再调用 C# 的委托
                _onComplete?.Invoke(session);
            }
        }
#endif

        private readonly HashSet<string> _supportedExtensions = new HashSet<string> { ".mp3", ".wav", ".pcm" };

        public async Task<string> PrepareAudioForTranscriptionAsync(string originalFilePath, string targetCodec = "libmp3lame")
        {
            var extension = Path.GetExtension(originalFilePath).ToLowerInvariant();

            if (extension == ".mp3" && targetCodec == "libmp3lame")
            {
                return originalFilePath;
            }
            if (_supportedExtensions.Contains(extension) && extension != ".mp3")
            {
                // wav/pcm aac等格式依然需要转换
            }


            try
            {
                string outputFileName = $"{Path.GetFileNameWithoutExtension(originalFilePath)}_{Guid.NewGuid()}.mp3";
                string outputFilePath = Path.Combine(FileSystem.CacheDirectory, outputFileName);

#if WINDOWS
                // =======================================================
                // WINDOWS IMPLEMENTATION (保持不变)
                // =======================================================
                await FFMpegArguments
                    .FromFileInput(originalFilePath)
                    .OutputToFile(outputFilePath, true, options => options
                        .WithAudioCodec(targetCodec) 
                    )
                    .ProcessAsynchronously();
                
                return outputFilePath;

#elif ANDROID
                // =======================================================
                // ANDROID IMPLEMENTATION (完全修正)
                // =======================================================
                var tcs = new TaskCompletionSource<FFmpegSession>();
                Action<FFmpegSession> completeAction = session => tcs.TrySetResult(session);

                // --- 2. 实例化我们的回调辅助类 ---
                var completeCallback = new FFmpegSessionCompleteCallback(completeAction);

                string command = $"-y -i \"{originalFilePath}\" -c:a {targetCodec} \"{outputFilePath}\"";

                // --- 3. 调用 ExecuteAsync，并传入回调对象 ---
                // 其他回调（log, statistics）我们不需要，所以传 null
                FFmpegKit.ExecuteAsync(command, completeCallback, null, null);

                var completedSession = await tcs.Task;

                // --- 4. 使用正确的属性和方法 ---
                if (ReturnCode.IsSuccess(completedSession.ReturnCode))
                {
                    return outputFilePath;
                }
                else
                {
                    Console.WriteLine($"[MediaConversionService] FFmpegKit failed with code {completedSession.ReturnCode}.");
                    // --- 使用正确的日志获取方法 ---
                    Console.WriteLine($"[MediaConversionService] Logs: {completedSession.Output}");
                    return null;
                }

#else
                Console.WriteLine($"[MediaConversionService] Media conversion is not implemented for the current platform.");
                return null;
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaConversionService] Exception during conversion: {ex.Message}");
                return null;
            }
        }
    }
}