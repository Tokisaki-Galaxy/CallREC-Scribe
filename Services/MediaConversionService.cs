using FFMpegCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CallREC_Scribe.Services
{
    public class MediaConversionService
    {
        private readonly HashSet<string> _supportedExtensions = new HashSet<string> { ".mp3", ".wav", ".pcm" };

        /// <summary>
        /// 检查文件并根据需要转换格式和重采样率，以匹配目标引擎。
        /// </summary>
        /// <param name="originalFilePath">原始音频文件的路径。</param>
        /// <param name="targetEngineModel">目标腾讯云引擎模型，例如 "8k_zh" 或 "16k_zh"。</param>
        /// <returns>一个完全准备好用于转录的文件路径，如果失败则返回 null。</returns>
        public async Task<string?> PrepareAudioForTranscriptionAsync(string originalFilePath, string targetEngineModel)
        {
            try
            {
                // 1. 确定目标采样率
                int targetSampleRate = targetEngineModel.StartsWith("8k") ? 8000 : 16000;

                // 2. 获取原始文件的信息 (包括格式和采样率)
                var mediaInfo = await FFProbe.AnalyseAsync(originalFilePath);
                var originalAudioStream = mediaInfo.AudioStreams.FirstOrDefault();
                if (originalAudioStream == null)
                {
                    Console.WriteLine($"[MediaConversionService] Could not find audio stream in {originalFilePath}");
                    return null;
                }
                int originalSampleRate = originalAudioStream.SampleRateHz;
                string originalExtension = Path.GetExtension(originalFilePath).ToLowerInvariant();

                // 3. 判断是否需要处理
                bool needsConversion = !_supportedExtensions.Contains(originalExtension);
                bool needsResampling = originalSampleRate != targetSampleRate;

                if (!needsConversion && !needsResampling)
                {
                    // 如果格式和采样率都已完美匹配，直接返回原始文件
                    return originalFilePath;
                }

                // 4. 执行转换/重采样
                string outputFileName = $"{Path.GetFileNameWithoutExtension(originalFilePath)}_{Guid.NewGuid()}.mp3";
                string outputFilePath = Path.Combine(FileSystem.CacheDirectory, outputFileName);

                await FFMpegArguments
                    .FromFileInput(originalFilePath)
                    .OutputToFile(outputFilePath, true, options =>
                    {
                        options.WithAudioCodec("libmp3lame");
                        // 如果需要，添加重采样参数到 FFmpeg 命令中
                        if (needsResampling)
                        {
                            options.WithAudioSamplingRate(targetSampleRate);
                        }
                    })
                    .ProcessAsynchronously();

                return outputFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaConversionService] Failed to process {originalFilePath}: {ex.Message}");
                return null;
            }
        }
    }
}