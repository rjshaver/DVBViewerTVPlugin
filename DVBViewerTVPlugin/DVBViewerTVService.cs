﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;

using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Plugins.DVBViewer.Services.Entities;

namespace MediaBrowser.Plugins.DVBViewer
{
    /// <summary>
    /// Provides DVBViewer Recording Service integration for Emby
    /// </summary>
    public class DVBViewerTvService : ILiveTvService
    {
        private static StreamingDetails _currentStreamDetails;
        private bool refreshTimers = false;

        public string HomePageUrl
        {
            get { return "http://www.dvbviewer.tv"; }
        }

        public string Name
        {
            get { return "DVBViewer (Recording Service)"; }
        }

    #region General

        public Task<LiveTvServiceStatusInfo> GetStatusInfoAsync(CancellationToken cancellationToken)
        {
            LiveTvServiceStatusInfo result;

            var configurationValidationResult = Plugin.Instance.Configuration.Validate();
            var pluginVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

            // Validate configuration first
            if (!configurationValidationResult.IsValid)
            {
                result = new LiveTvServiceStatusInfo()
                {
                    HasUpdateAvailable = false,
                    Status = LiveTvServiceStatus.Unavailable,
                    StatusMessage = "Cannot connect to DVBViewer Recording Service - check your settings",
                    Version = String.Format("DVBViewer Live TV Plugin V{0}", pluginVersion)
                };
            }
            else
            {
                try
                {
                    var serviceVersion = Plugin.TvProxy.GetStatusInfo(cancellationToken).Version();

                    result = new LiveTvServiceStatusInfo()
                    {
                        HasUpdateAvailable = false,
                        Status = LiveTvServiceStatus.Ok,
                        StatusMessage = "Successfully connected to DVBViewer Recording Service API",
                        Version = String.Format("DVBViewer Live TV Plugin V{0} - {1}", pluginVersion, serviceVersion)
                    };

                }
                catch (Exception ex)
                {
                    Plugin.Logger.Error(ex, "Exception occured getting the DVBViewer Recording Service status");

                    result = new LiveTvServiceStatusInfo()
                    {
                        HasUpdateAvailable = false,
                        Status = LiveTvServiceStatus.Unavailable,
                        StatusMessage = "Cannot connect to DVBViewer Recording Service - check your settings",
                        Version = String.Format("DVBViewer Live TV Plugin V{0}", pluginVersion)
                    };
                }
            }

            return Task.FromResult(result);
        }

        public Task ResetTuner(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

    #endregion

    #region Channels

        public Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Plugin.TvProxy.GetChannels(cancellationToken));
        }

        public Task<ImageStream> GetChannelImageAsync(string channelId, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(Plugin.TvProxy.GetChannelLogo(channelId, cancellationToken));
            }
            catch (System.NullReferenceException)
            {
                throw;
            }
        }

        public Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
        {
            return Task.FromResult(Plugin.TvProxy.GetPrograms(cancellationToken, channelId, startDateUtc, endDateUtc));
        }

        public Task<ImageStream> GetProgramImageAsync(string programId, string channelId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

    #endregion

    #region Recordings

        public Task<IEnumerable<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Plugin.TvProxy.GetRecordings(cancellationToken));
        }

        public Task<ImageStream> GetRecordingImageAsync(string recordingId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task DeleteRecordingAsync(string recordingId, CancellationToken cancellationToken)
        {
            Plugin.TvProxy.DeleteRecording(recordingId, cancellationToken);
            return Task.Delay(0, cancellationToken);
        }

    #endregion

    #region Timers

        public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo program)
        {
            var scheduleDefaults = Plugin.TvProxy.GetScheduleDefaults(cancellationToken);
            var scheduleDayOfWeek = new List<DayOfWeek>();

            if (program != null)
                scheduleDayOfWeek.Add(program.StartDate.ToLocalTime().DayOfWeek);

            return Task.FromResult(new SeriesTimerInfo()
            {
                IsPostPaddingRequired = scheduleDefaults.PostRecordInterval.Ticks > 0,
                IsPrePaddingRequired = scheduleDefaults.PreRecordInterval.Ticks > 0,
                PostPaddingSeconds = (Int32)scheduleDefaults.PostRecordInterval.TotalSeconds,
                PrePaddingSeconds = (Int32)scheduleDefaults.PreRecordInterval.TotalSeconds,
                RecordNewOnly = true,
                RecordAnyChannel = false,
                RecordAnyTime = false,
                Days = scheduleDayOfWeek,
                SkipEpisodesInLibrary = false,
            });
        }

        public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        {
            if (Plugin.Instance.Configuration.EnableTimerCache)
            {
                var timerCache = MemoryCache.Default;

                if (refreshTimers)
                {
                    timerCache.Remove("timers");
                    refreshTimers = false;
                }

                if (!timerCache.Contains("timers"))
                {
                    Plugin.Logger.Info("Add timers to memory cache");
                    var expiration = DateTimeOffset.UtcNow.AddSeconds(20);
                    var results = Plugin.TvProxy.GetSchedules(cancellationToken);

                    timerCache.Add("timers", Task.FromResult(results), expiration);
                }

                Plugin.Logger.Info("Return timers from memory cache");
                return (Task<IEnumerable<TimerInfo>>)timerCache.Get("timers", null);
            }
            else
            {
                return Task.FromResult(Plugin.TvProxy.GetSchedules(cancellationToken));
            }
        }

        public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            refreshTimers = true;
            Plugin.TvProxy.CreateSchedule(cancellationToken, info);
            return Task.Delay(0, cancellationToken);
        }

        public Task UpdateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            refreshTimers = true;
            Plugin.TvProxy.ChangeSchedule(cancellationToken, info);
            return Task.Delay(0, cancellationToken);
        }

        public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            refreshTimers = true;
            Plugin.TvProxy.DeleteSchedule(cancellationToken, timerId);
            return Task.Delay(0, cancellationToken);
        }

        public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Plugin.TvProxy.GetSeriesSchedules(cancellationToken));
        }

        public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            Plugin.TvProxy.CreateSeriesSchedule(cancellationToken, info);
            return Task.Delay(0, cancellationToken);
        }

        public Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            Plugin.TvProxy.ChangeSeriesSchedule(cancellationToken, info);
            return Task.Delay(0, cancellationToken);
        }

        public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            Plugin.TvProxy.DeleteSeriesSchedule(cancellationToken, timerId);
            return Task.Delay(0, cancellationToken);
        }

    #endregion

    #region Streaming

        public Task<MediaSourceInfo> GetChannelStream(string channelId, string streamId, CancellationToken cancellationToken)
        {
            _currentStreamDetails = Plugin.StreamingProxy.GetLiveTvStream(cancellationToken, channelId);
            return Task.FromResult(_currentStreamDetails.SourceInfo);
        }

        public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<MediaSourceInfo> GetRecordingStream(string recordingId, string streamId, CancellationToken cancellationToken)
        {
            _currentStreamDetails = Plugin.StreamingProxy.GetRecordingStream(cancellationToken, recordingId, TimeSpan.Zero);
            return Task.FromResult(_currentStreamDetails.SourceInfo);
        }

        public Task<List<MediaSourceInfo>> GetRecordingStreamMediaSources(string recordingId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task RecordLiveStream(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task CloseLiveStream(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

    #endregion

    #region Events

        public event EventHandler<RecordingStatusChangedEventArgs> RecordingStatusChanged;

        public event EventHandler DataSourceChanged;

    #endregion

    }
}