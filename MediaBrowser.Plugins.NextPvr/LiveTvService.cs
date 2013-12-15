﻿using MediaBrowser.Common.Net;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Plugins.NextPvr.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MediaBrowser.Plugins.NextPvr
{
    /// <summary>
    /// Class LiveTvService
    /// </summary>
    public class LiveTvService : ILiveTvService
    {
        private readonly IHttpClient _httpClient;
        private readonly IJsonSerializer _jsonSerializer;

        private string Sid { get; set; }

        private string WebserviceUrl { get; set; }
        private string Pin { get; set; }

        public LiveTvService(IHttpClient httpClient, IJsonSerializer jsonSerializer)
        {
            _httpClient = httpClient;
            _jsonSerializer = jsonSerializer;
        }

        private async Task EnsureConnectionAsync(CancellationToken cancellationToken)
        {
            var config = Plugin.Instance.Configuration;

            if (string.IsNullOrEmpty(config.WebServiceUrl))
            {
                throw new InvalidOperationException("NextPvr web service url must be configured.");
            }

            if (string.IsNullOrEmpty(Sid))
            {
                await InitiateSession(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Initiate the nextPvr session
        /// </summary>
        private async Task InitiateSession(CancellationToken cancellationToken)
        {
            string html;

            WebserviceUrl = Plugin.Instance.Configuration.WebServiceUrl;
            Pin = Plugin.Instance.Configuration.Pin;

            var options = new HttpRequestOptions
            {
                // This moment only device name xbmc is available
                Url = string.Format("{0}/service?method=session.initiate&ver=1.0&device=xbmc", WebserviceUrl),

                CancellationToken = cancellationToken
            };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml.ToLower() == "ok")
            {
                var salt = XmlHelper.GetSingleNode(html, "//rsp/salt").InnerXml;
                var sid = XmlHelper.GetSingleNode(html, "//rsp/sid").InnerXml;

                var loggedIn = await Login(sid, salt, cancellationToken).ConfigureAwait(false);

                if (loggedIn)
                {
                    Sid = sid;
                }
            }
        }

        private async Task<bool> Login(string sid, string salt, CancellationToken cancellationToken)
        {
            string html;

            var md5 = EncryptionHelper.GetMd5Hash(Pin);
            var strb = new StringBuilder();

            strb.Append(":");
            strb.Append(md5);
            strb.Append(":");
            strb.Append(salt);

            var md5Result = EncryptionHelper.GetMd5Hash(strb.ToString());

            var options = new HttpRequestOptions()
                {
                    // This moment only device name xbmc is available
                    Url =
                        string.Format("{0}/service?method=session.login&&sid={1}&md5={2}", WebserviceUrl, sid, md5Result),

                    CancellationToken = cancellationToken
                };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            return XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml.ToLower() == "ok";
        }

        /// <summary>
        /// Gets the channels async.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task{IEnumerable{ChannelInfo}}.</returns>
        public async Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
        {
            var options = new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                Url = string.Format("{0}/public/GuideService/Channels", Plugin.Instance.Configuration.WebServiceUrl)
            };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                return new ChannelResponse().GetChannels(stream, _jsonSerializer);
            }
        }

        public async Task<IEnumerable<RecordingInfo>> GetRecordingsAsync(CancellationToken cancellationToken)
        {
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var recordings = new List<RecordingInfo>();

            string html;

            var options = new HttpRequestOptions()
                {
                    CancellationToken = cancellationToken,
                    Url = string.Format("{0}/service?method=recording.list&filter=ready&sid={1}", WebserviceUrl, Sid)
                };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (string.Equals(XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml, "ok", StringComparison.OrdinalIgnoreCase))
            {
                recordings.AddRange(
                    from XmlNode node in XmlHelper.GetMultipleNodes(html, "//rsp/recordings/recording")
                    let startDate = DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//start_time").InnerXml)
                    select new RecordingInfo()
                        {
                            Id = XmlHelper.GetSingleNode(node.OuterXml, "//id").InnerXml,
                            Name = XmlHelper.GetSingleNode(node.OuterXml, "//name").InnerXml,
                            Overview = XmlHelper.GetSingleNode(node.OuterXml, "//desc").InnerXml,
                            ProgramId = GetString(node, "epg_event_oid"),
                            StartDate = startDate,
                            Status = GetStatus(node),
                            ChannelName = XmlHelper.GetSingleNode(node.OuterXml, "//channel").InnerXml,
                            ChannelId = XmlHelper.GetSingleNode(node.OuterXml, "//channel_id").InnerXml,
                            HasImage = false,
                            //IsRecurring = bool.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring").InnerXml),
                            //RecurrringStartDate =
                            //DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring_start").InnerXml),
                            //RecurringEndDate =
                            //DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring_end").InnerXml),
                            //RecurringParent = XmlHelper.GetSingleNode(node.OuterXml, "//recurring_parent").InnerXml,
                            //DayMask = XmlHelper.GetSingleNode(node.OuterXml, "//daymask").InnerXml.Split(',').ToList(),
                            EndDate =
                                startDate.AddSeconds(
                                    (double.Parse(
                                        XmlHelper.GetSingleNode(node.OuterXml, "//duration_seconds").InnerXml)))
                        });
            }

            return recordings;
        }

        private RecordingStatus GetStatus(XmlNode node)
        {
            node = XmlHelper.GetSingleNode(node.OuterXml, "//status");

            var statusText = node == null ? string.Empty : node.InnerXml;

            if (string.Equals(statusText, "COMPLETED", StringComparison.OrdinalIgnoreCase) || string.Equals(statusText, "READY", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.Completed;
            }
            if (string.Equals(statusText, "COMPLETED_WITH_ERROR", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.Error;
            }
            if (string.Equals(statusText, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.InProgress;
            }
            if (string.Equals(statusText, "CONFLICT", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.ConflictedNotOk;
            }
            if (string.Equals(statusText, "DELETED", StringComparison.OrdinalIgnoreCase))
            {
                return RecordingStatus.Cancelled;
            }

            // TODO : Parse this
            return RecordingStatus.Scheduled;
        }

        private string GetString(XmlNode node, string name)
        {
            node = XmlHelper.GetSingleNode(node.OuterXml, "//" + name);

            return node == null ? null : node.InnerXml;
        }

        private async Task CancelRecordingAsync(string recordingId, CancellationToken cancellationToken)
        {
            string html;

            var options = new HttpRequestOptions()
                {
                    CancellationToken = cancellationToken,
                    Url =
                        string.Format("{0}/service?method=recording.delete&recording_id={1}&sid={2}", WebserviceUrl,
                                      recordingId, Sid)
                };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (!string.Equals(XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationException("Operation failed");
            }
        }

        public Task DeleteRecordingAsync(string recordingId, CancellationToken cancellationToken)
        {
            return CancelRecordingAsync(recordingId, cancellationToken);
        }

        public async Task ScheduleRecordingAsync(string name, string channelId, DateTime startTime, TimeSpan duration, CancellationToken cancellationToken)
        {
            string html;

            var options = new HttpRequestOptions()
                {
                    CancellationToken = cancellationToken,
                    Url =
                        string.Format("{0}/service?method=recording.save&name={1}&channel={2}&time_t={3}&duration={4}&sid={5}", WebserviceUrl,
                                      name, channelId, startTime, duration, Sid)
                };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (!string.Equals(XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationException("Operation failed");
            }
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get { return "Next Pvr"; }
        }

        public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(string channelId, CancellationToken cancellationToken)
        {
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var epgInfos = new List<ProgramInfo>();

            string html;

            var options = new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                Url =
                    string.Format("{0}/service?method=channel.listings&channel_id={1}&sid={2}", WebserviceUrl,
                                  channelId, Sid)
            };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (string.Equals(XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml, "ok", StringComparison.OrdinalIgnoreCase))
            {
                epgInfos.AddRange(
                    from XmlNode node in XmlHelper.GetMultipleNodes(html, "//rsp/listings/l")
                    let startDate = XmlHelper.GetSingleNode(node.OuterXml, "//start").InnerXml
                    let endDate = XmlHelper.GetSingleNode(node.OuterXml, "//end").InnerXml
                    select new ProgramInfo()
                    {
                        Id = XmlHelper.GetSingleNode(node.OuterXml, "//id").InnerXml,
                        ChannelId = channelId,
                        Name = XmlHelper.GetSingleNode(node.OuterXml, "//name").InnerXml,
                        Overview = XmlHelper.GetSingleNode(node.OuterXml, "//description").InnerXml,
                        StartDate =
                            new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(Math.Round(double.Parse(startDate)) /
                                                                            1000d).ToLocalTime(),
                        EndDate =
                            new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds(Math.Round(double.Parse(endDate)) / 1000d)
                                                                .ToLocalTime(),
                        Genres = GetGenres(node),
                        HasImage = false
                    });
            }

            return epgInfos;
        }

        private List<string> GetGenres(XmlNode node)
        {
            var list = new List<string>();

            node = XmlHelper.GetSingleNode(node.OuterXml, "//genre");

            if (node != null)
            {
                list.Add(node.InnerXml);
            }
            return list;
        }

        public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            return DeleteRecordingAsync(timerId, cancellationToken);
        }

        public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            var duration = info.EndDate - info.StartDate;

            return ScheduleRecordingAsync(info.Name, info.ChannelId, info.StartDate, duration, cancellationToken);
        }

        public async Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        {
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var recordings = new List<TimerInfo>();

            string html;

            var options = new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                Url = string.Format("{0}/service?method=recording.list&filter=pending&sid={1}", WebserviceUrl, Sid)
            };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (string.Equals(XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml, "ok", StringComparison.OrdinalIgnoreCase))
            {
                recordings.AddRange(
                    from XmlNode node in XmlHelper.GetMultipleNodes(html, "//rsp/recordings/recording")
                    let startDate = DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//start_time").InnerXml)
                    select new TimerInfo()
                    {
                        Id = XmlHelper.GetSingleNode(node.OuterXml, "//id").InnerXml,
                        Name = XmlHelper.GetSingleNode(node.OuterXml, "//name").InnerXml,
                        Overview = XmlHelper.GetSingleNode(node.OuterXml, "//desc").InnerXml,
                        ProgramId = GetString(node, "epg_event_oid"),
                        StartDate = startDate,
                        Status = GetStatus(node),
                        ChannelName = XmlHelper.GetSingleNode(node.OuterXml, "//channel").InnerXml,
                        ChannelId = XmlHelper.GetSingleNode(node.OuterXml, "//channel_id").InnerXml,
                        //IsRecurring = bool.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring").InnerXml),
                        //RecurrringStartDate =
                        //DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring_start").InnerXml),
                        //RecurringEndDate =
                        //DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring_end").InnerXml),
                        SeriesTimerId = GetString(node, "recurring_parent"),
                        //DayMask = XmlHelper.GetSingleNode(node.OuterXml, "//daymask").InnerXml.Split(',').ToList(),
                        EndDate =
                            startDate.AddSeconds(
                                (double.Parse(
                                    XmlHelper.GetSingleNode(node.OuterXml, "//duration_seconds").InnerXml)))
                    });
            }

            return recordings;
        }

        public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        {
            await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);

            var recordings = new List<SeriesTimerInfo>();

            string html;

            var options = new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                Url = string.Format("{0}/service?method=recording.recurring.list&sid={1}", WebserviceUrl, Sid)
            };

            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                using (var reader = new StreamReader(stream))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (string.Equals(XmlHelper.GetSingleNode(html, "//rsp/@stat").InnerXml, "ok", StringComparison.OrdinalIgnoreCase))
            {
                recordings.AddRange(
                    from XmlNode node in XmlHelper.GetMultipleNodes(html, "//rsp/recurrings/recurring")
                    select new SeriesTimerInfo()
                    {
                        Id = GetString(node, "id"),
                        Name = GetString(node, "name"),
                        StartDate = DateTime.Parse(GetString(node, "matchrules/Rules/StartTime")),
                        ChannelName = GetSeriesChannelName(node),
                        ChannelId = GetSeriesChannelId(node),
                        //IsRecurring = bool.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring").InnerXml),
                        //RecurrringStartDate =
                        //DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring_start").InnerXml),
                        //RecurringEndDate =
                        //DateTime.Parse(XmlHelper.GetSingleNode(node.OuterXml, "//recurring_end").InnerXml),
                        //DayMask = XmlHelper.GetSingleNode(node.OuterXml, "//daymask").InnerXml.Split(',').ToList(),
                        EndDate = DateTime.Parse(GetString(node, "matchrules/Rules/EndTime")),
                        Days = ParseDays(GetString(node, "matchrules/Rules/Days")),
                        RequestedPrePaddingSeconds = int.Parse(GetString(node, "matchrules/Rules/PrePadding")) * 60,
                        RequestedPostPaddingSeconds = int.Parse(GetString(node, "matchrules/Rules/PostPadding")) * 60,
                        RecurrenceType = GetRecurrenceType(node)
                    });
            }

            return recordings;
        }

        private string GetSeriesChannelName(XmlNode node)
        {
            var val = GetString(node, "matchrules/Rules/ChannelName");

            if (string.Equals(val, "All Channels", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return val;
        }

        private string GetSeriesChannelId(XmlNode node)
        {
            var val = GetString(node, "matchrules/Rules/ChannelOID");

            if (string.Equals(val, "0", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return val;
        }

        private RecurrenceType GetRecurrenceType(XmlNode node)
        {
            var days = ParseDays(GetString(node, "matchrules/Rules/Days"));

            var onlyNew = GetString(node, "matchrules/Rules/OnlyNewEpisodes");

            if (days.Count > 0)
            {
                return RecurrenceType.Manual;
            }

            var channelId = GetString(node, "matchrules/Rules/ChannelOID");

            if (string.Equals(channelId, "0", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(onlyNew, "true", StringComparison.OrdinalIgnoreCase) ?
                    RecurrenceType.NewProgramEventsAllChannels :
                    RecurrenceType.AllProgramEventsAllChannels;
            }

            return string.Equals(onlyNew, "true", StringComparison.OrdinalIgnoreCase) ?
                RecurrenceType.NewProgramEventsOneChannel :
                RecurrenceType.AllProgramEventsOneChannel;
        }

        private List<DayOfWeek> ParseDays(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new List<DayOfWeek>();
            }

            return value.Split(',')
                .Select(i => (DayOfWeek)Enum.Parse(typeof(DayOfWeek), i.Trim(), true))
                .ToList();
        }

        public Task UpdateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ImageResponseInfo> GetProgramImageAsync(string programId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<ImageResponseInfo> GetChannelImageAsync(string channelId, CancellationToken cancellationToken)
        {
            ImageResponseInfo responseInfo;

            var options = new HttpRequestOptions()
            {
                CancellationToken = cancellationToken,
                AcceptHeader = "image/*",
                Url = string.Format("{0}/service?method=channel.icon&channel_id={1}&sid={2}", WebserviceUrl, channelId, Sid)
            };


            using (var stream = await _httpClient.Get(options).ConfigureAwait(false))
            {
                responseInfo = new ImageResponseInfo
                {
                    Stream = stream,
                    MimeType = "image/jpg"
                };
            }

            return responseInfo;
        }


        public Task<ImageResponseInfo> GetRecordingImageAsync(string channelId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task UpdateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
