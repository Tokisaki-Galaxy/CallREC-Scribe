// 文件名: Services/MediaConversionService.cs

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

#if ANDROID
using Android.Media;
#endif

namespace CallREC_Scribe.Services
{
    public class MediaConversionService
    {
        public async Task<string> PrepareAudioForTranscriptionAsync(string originalFilePath, string engineModelType)
        {
            int targetSampleRate = engineModelType.StartsWith("8k", StringComparison.OrdinalIgnoreCase) ? 8000 : 16000;
            string outputFileName = $"{Guid.NewGuid()}.wav";
            string outputFilePath = Path.Combine(FileSystem.CacheDirectory, outputFileName);

            Debug.WriteLine($"[MediaConversionService] 开始转换为 WAV 格式。目标采样率: {targetSampleRate} Hz");

            try
            {
                await Task.Run(() =>
                {
#if ANDROID
                    DecodeAndResampleToWavOnAndroid(originalFilePath, outputFilePath, targetSampleRate);
#elif WINDOWS
                    ResampleToWavOnWindows(originalFilePath, outputFilePath, targetSampleRate);
#else
                    throw new NotSupportedException("当前平台不支持媒体转换。");
#endif
                });

                Debug.WriteLine($"[MediaConversionService] 转换成功完成！输出文件: {outputFilePath}");
                return outputFilePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaConversionService] 转换失败: {ex.Message}");
                Debug.WriteLine($"[MediaConversionService] 堆栈跟踪: {ex.StackTrace}");
                return null;
            }
        }

#if ANDROID
        private void DecodeAndResampleToWavOnAndroid(string inputPath, string outputPath, int targetSampleRate)
        {
            string tempPcmPath = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid()}.tmp");

            // --- 原生解码部分保持不变 ---
            // (此处省略了与上一版完全相同的原生解码代码，以保持简洁)
            MediaExtractor extractor = null;
            MediaCodec codec = null;
            try
            {
                extractor = new MediaExtractor();
                extractor.SetDataSource(inputPath);
                MediaFormat format = null;
                int audioTrackIndex = -1;
                for (int i = 0; i < extractor.TrackCount; i++) { format = extractor.GetTrackFormat(i); if (format.GetString(MediaFormat.KeyMime).StartsWith("audio/")) { audioTrackIndex = i; break; } }
                if (audioTrackIndex == -1) throw new InvalidOperationException("在文件中找不到音频轨道。");
                extractor.SelectTrack(audioTrackIndex);
                string mimeType = format.GetString(MediaFormat.KeyMime);
                codec = MediaCodec.CreateDecoderByType(mimeType);
                codec.Configure(format, null, null, 0);
                codec.Start();
                using (var fs = new FileStream(tempPcmPath, FileMode.Create, FileAccess.Write))
                {
                    var bufferInfo = new MediaCodec.BufferInfo(); bool isEos = false;
                    while (!isEos)
                    {
                        int inIndex = codec.DequeueInputBuffer(10000); if (inIndex >= 0)
                        {
                            var buffer = codec.GetInputBuffer(inIndex); int sampleSize = extractor.ReadSampleData(buffer, 0);
                            if (sampleSize < 0) { codec.QueueInputBuffer(inIndex, 0, 0, 0, MediaCodecBufferFlags.EndOfStream); } else { codec.QueueInputBuffer(inIndex, 0, sampleSize, extractor.SampleTime, 0); extractor.Advance(); }
                        }
                        int outIndex = codec.DequeueOutputBuffer(bufferInfo, 10000); if (outIndex >= 0)
                        {
                            if ((bufferInfo.Flags & MediaCodecBufferFlags.EndOfStream) != 0) isEos = true;
                            var outBuffer = codec.GetOutputBuffer(outIndex); var chunk = new byte[bufferInfo.Size]; outBuffer.Get(chunk); outBuffer.Clear(); fs.Write(chunk, 0, chunk.Length); codec.ReleaseOutputBuffer(outIndex, false);
                        }
                    }
                }
                Debug.WriteLine($"[MediaConversionService] Android 原生解码完成。");

                // --- 纯C#音频处理流水线 ---
                int originalSampleRate = format.GetInteger(MediaFormat.KeySampleRate);
                int originalChannels = format.GetInteger(MediaFormat.KeyChannelCount);
                var rawFormat = new WaveFormat(originalSampleRate, 16, originalChannels);

                using (var reader = new RawSourceWaveStream(File.OpenRead(tempPcmPath), rawFormat))
                {
                    // 1. 将旧的 IWaveProvider 转换为现代的 ISampleProvider
                    ISampleProvider sampleProvider = reader.ToSampleProvider();

                    // 2. 如果是立体声，使用纯C#的转换器转为单声道
                    if (sampleProvider.WaveFormat.Channels > 1)
                    {
                        sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
                    }

                    // 3. 使用100%纯C#的 WdlResamplingSampleProvider 进行重采样
                    var resampler = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);

                    // 4. 使用纯C#的 WaveFileWriter 将最终的音频流写入WAV文件
                    //    CreateWaveFile16 会自动处理从 ISampleProvider 到 16-bit WAV 的转换
                    WaveFileWriter.CreateWaveFile16(outputPath, resampler);
                }
                Debug.WriteLine($"[MediaConversionService] NAudio 纯C#处理流水线完成。");
            }
            finally
            {
                codec?.Stop(); codec?.Release();
                extractor?.Release();
                if (File.Exists(tempPcmPath)) File.Delete(tempPcmPath);
            }
        }
#endif

#if WINDOWS
        private void ResampleToWavOnWindows(string inputPath, string outputPath, int targetSampleRate)
        {
            // 在Windows上，我们也可以使用相同的纯C#流水线，以保持代码一致和稳定
            using (var reader = new AudioFileReader(inputPath))
            {
                ISampleProvider sampleProvider = reader;
                if (sampleProvider.WaveFormat.Channels > 1)
                {
                    sampleProvider = new StereoToMonoSampleProvider(sampleProvider);
                }
                var resampler = new WdlResamplingSampleProvider(sampleProvider, targetSampleRate);
                WaveFileWriter.CreateWaveFile16(outputPath, resampler);
            }
        }
#endif
    }
}