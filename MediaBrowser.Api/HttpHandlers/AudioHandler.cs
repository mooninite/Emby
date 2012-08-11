﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using MediaBrowser.Common.Logging;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Net.Handlers;
using MediaBrowser.Controller;
using MediaBrowser.Model.Entities;

namespace MediaBrowser.Api.HttpHandlers
{
    public class AudioHandler : BaseMediaHandler<Audio>
    {
        public IEnumerable<string> AudioFormats
        {
            get
            {
                string val = QueryString["audioformats"];

                if (string.IsNullOrEmpty(val))
                {
                    return new string[] { "mp3" };
                }

                return val.Split(',');
            }
        }

        public IEnumerable<int> AudioBitRates
        {
            get
            {
                string val = QueryString["audioformats"];

                if (string.IsNullOrEmpty(val))
                {
                    return new int[] { };
                }

                return val.Split(',').Select(v => int.Parse(v));
            }
        }

        private int? GetMaxAcceptedBitRate(string audioFormat)
        {
            int index = AudioFormats.ToList().IndexOf(audioFormat);

            return AudioBitRates.ElementAtOrDefault(index);
        }

        /// <summary>
        /// Determines whether or not the original file requires transcoding
        /// </summary>
        protected override bool RequiresConversion()
        {
            string currentFormat = Path.GetExtension(LibraryItem.Path).Replace(".", string.Empty);

            // If it's not in a format the consumer accepts, return true
            if (!AudioFormats.Any(f => currentFormat.EndsWith(f, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            int? bitrate = GetMaxAcceptedBitRate(currentFormat);

            // If the bitrate is greater than our desired bitrate, we need to transcode
            if (bitrate.HasValue && bitrate.Value < LibraryItem.BitRate)
            {
                return true;
            }

            // If the number of channels is greater than our desired channels, we need to transcode
            if (AudioChannels.HasValue && AudioChannels.Value < LibraryItem.Channels)
            {
                return true;
            }

            // If the sample rate is greater than our desired sample rate, we need to transcode
            if (AudioSampleRate.HasValue && AudioSampleRate.Value < LibraryItem.SampleRate)
            {
                return true;
            }

            // Yay
            return false;
        }

        /// <summary>
        /// Gets the format we'll be converting to
        /// </summary>
        protected override string GetOutputFormat()
        {
            return AudioFormats.First();
        }

        /// <summary>
        /// Creates arguments to pass to ffmpeg
        /// </summary>
        private string GetAudioArguments()
        {
            List<string> audioTranscodeParams = new List<string>();

            string outputFormat = GetOutputFormat();

            int? bitrate = GetMaxAcceptedBitRate(outputFormat);

            if (bitrate.HasValue)
            {
                audioTranscodeParams.Add("-ab " + bitrate.Value);
            }

            if (AudioChannels.HasValue)
            {
                audioTranscodeParams.Add("-ac " + AudioChannels.Value);
            }

            if (AudioSampleRate.HasValue)
            {
                audioTranscodeParams.Add("-ar " + AudioSampleRate.Value);
            }

            audioTranscodeParams.Add("-f " + outputFormat);

            return "-i \"" + LibraryItem.Path + "\" -vn " + string.Join(" ", audioTranscodeParams.ToArray()) + " -";
        }

        protected async override Task WriteResponseToOutputStream(Stream stream)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();

            startInfo.CreateNoWindow = true;

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;

            startInfo.FileName = ApiService.FFMpegPath;
            startInfo.WorkingDirectory = ApiService.FFMpegDirectory;
            startInfo.Arguments = GetAudioArguments();

            Logger.LogInfo(startInfo.FileName + " " + startInfo.Arguments);

            Process process = new Process();
            process.StartInfo = startInfo;

            try
            {
                process.Start();

                await process.StandardOutput.BaseStream.CopyToAsync(stream);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public abstract class BaseMediaHandler<T> : BaseHandler
        where T : BaseItem, new()
    {
        private T _LibraryItem;
        /// <summary>
        /// Gets the library item that will be played, if any
        /// </summary>
        protected T LibraryItem
        {
            get
            {
                if (_LibraryItem == null)
                {
                    string id = QueryString["id"];

                    if (!string.IsNullOrEmpty(id))
                    {
                        _LibraryItem = Kernel.Instance.GetItemById(Guid.Parse(id)) as T;
                    }
                }

                return _LibraryItem;
            }
        }

        public int? AudioChannels
        {
            get
            {
                string val = QueryString["audiochannels"];

                if (string.IsNullOrEmpty(val))
                {
                    return null;
                }

                return int.Parse(val);
            }
        }

        public int? AudioSampleRate
        {
            get
            {
                string val = QueryString["audiosamplerate"];

                if (string.IsNullOrEmpty(val))
                {
                    return 44100;
                }

                return int.Parse(val);
            }
        }

        public override string ContentType
        {
            get
            {
                return MimeTypes.GetMimeType("." + GetOutputFormat());
            }
        }

        public override bool CompressResponse
        {
            get
            {
                return false;
            }
        }

        public override void ProcessRequest(HttpListenerContext ctx)
        {
            HttpListenerContext = ctx;

            if (!RequiresConversion())
            {
                new StaticFileHandler() { Path = LibraryItem.Path }.ProcessRequest(ctx);
                return;
            }

            base.ProcessRequest(ctx);
        }

        protected abstract string GetOutputFormat();
        protected abstract bool RequiresConversion();
    }
}
