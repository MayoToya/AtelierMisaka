﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AtelierMisaka.Cef;
using AtelierMisaka.Models;
using CefSharp;
using CefSharp.Wpf;
using Newtonsoft.Json;

namespace AtelierMisaka
{
    public class PatreonUtils : BaseUtils
    {
        private ChromiumWebBrowser _cwb;
        private bool _needLogin = false;
        private readonly Regex _cidRegex = new Regex(@"self"": ""https://www.patreon.com/api/campaigns/{\d+}");
        private readonly Regex _emailRegex = new Regex(@"email"": ""(.+?)""");
        private readonly Regex _htmlImg = new Regex(@"<p><img.+?></p>");
        private readonly string _postUrl = "https://www.patreon.com/api/posts?include=attachments.null%2Cmedia.null&filter[campaign_id]={0}&sort=-published_at&fields[post]=Ccomment_count%2Ccontent%2Ccurrent_user_can_view%2Ccurrent_user_has_liked%2Cembed%2Cimage%2Cpublished_at%2Cpost_type%2Cthumbnail_url%2Cteaser_text%2Ctitle%2Curl&json-api-use-default-includes=false";
        private readonly string _nextUrl = "https://www.patreon.com/api/posts?include=attachments.null%2Cmedia.null&filter[campaign_id]={0}&page[cursor]={1}&sort=-published_at&fields[post]=Ccomment_count%2Ccontent%2Ccurrent_user_can_view%2Ccurrent_user_has_liked%2Cembed%2Cimage%2Cpublished_at%2Cpost_type%2Cthumbnail_url%2Cteaser_text%2Ctitle%2Curl&json-api-use-default-includes=false";
        private readonly string _artistUrl = "https://www.patreon.com/api/campaigns/{0}?include=null";
        private readonly string _pledgeUrl = "https://www.patreon.com/api/pledges?include=campaign&fields[campaign]=name%2Curl&fields[pledge]=amount_cents%2Cpledge_cap_cents&json-api-use-default-includes=false";
        private readonly string _webCharSet = "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"/>";

        private Dictionary<string, string> _unicodeDic = new Dictionary<string, string>();

        public async Task<ResultMessage> InitBrowser()
        {
            try
            {
                if (null != GlobalData.VM_MA.PatreonCefBrowser)
                {
                    _cwb = (ChromiumWebBrowser)GlobalData.VM_MA.PatreonCefBrowser;
                    if (!GlobalData.VM_MA.IsInitialized)
                    {
                        if (GlobalData.VM_MA.UseProxy)
                        {
                            while (!_cwb.IsBrowserInitialized)
                            {
                                await Task.Delay(100);
                            }
                            if (!await CefHelper.SetProxy(_cwb, GlobalData.VM_MA.Proxy))
                            {
                                return ResultHelper.WebError($"无法设置代理{Environment.NewLine}请联系开发者");
                            }
                        }
                        return await LoginCheck(await GetWebCode("view-source:https://www.patreon.com/"));
                    }
                    return ResultHelper.NoError(_needLogin);
                }
                CefHelper.Initialize();
                _cwb = new ChromiumWebBrowser();
                GlobalData.VM_MA.PatreonCefBrowser = _cwb;
                if (GlobalData.VM_MA.UseProxy)
                {
                    while (!_cwb.IsBrowserInitialized)
                    {
                        await Task.Delay(100);
                    }
                    if (!await CefHelper.SetProxy(_cwb, GlobalData.VM_MA.Proxy))
                    {
                        return ResultHelper.WebError($"无法设置代理{Environment.NewLine}请联系开发者");
                    }
                }
                return await LoginCheck(await GetWebCode("view-source:https://www.patreon.com/"));
            }
            catch (Exception ex)
            {
                GlobalData.ErrorLog(ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine + "-----------------------------------------------");
                return ResultHelper.UnKnownError();
            }
        }

        private async Task<ResultMessage> LoginCheck(string htmlc)
        {
            if (!_needLogin)
            {
                if (htmlc.Contains("currentUser\": null"))
                {
                    _needLogin = true;
                    return await LoginCheck(await GetWebCode("https://www.patreon.com/login"));
                }
                else
                {
                    if (string.IsNullOrEmpty(htmlc))
                    {
                        return ResultHelper.WebError("网络错误，请重试");
                    }
                    Match ma = _emailRegex.Match(htmlc);
                    if (ma.Success)
                    {
                        var s = ma.Groups[1].Value;
                        if (!string.IsNullOrEmpty(GlobalData.VM_MA.Cookies))
                        {
                            if (s != GlobalData.VM_MA.Cookies)
                            {
                                return await LoginCheck(await GetWebCode("https://www.patreon.com/logout?ru=%2Fhome"));
                            }
                        }
                        else
                        {
                            GlobalData.VM_MA.Cookies = s;
                        }
                        GlobalData.VM_MA.IsInitialized = true;
                        return ResultHelper.NoError(_needLogin);
                    }
                    return ResultHelper.CookieError("找不到设置的邮箱地址");
                }
            }
            else
            {
                GlobalData.VM_MA.ShowLogin = true;
                _cwb.FrameLoadEnd += CWebBrowser_LoginCheck;
                _cwb.FrameLoadStart += Cwb_FrameLoadStart;
                return ResultHelper.NoError(_needLogin);
            }
        }

        private void Cwb_FrameLoadStart(object sender, FrameLoadStartEventArgs e)
        {
            if (e.Url == "https://www.patreon.com/home" || e.Url == "https://www.patreon.com/creator-home")
            {
                _cwb.Stop();
                _cwb.FrameLoadStart -= Cwb_FrameLoadStart;
                _cwb.Load("view-source:" + e.Url);
            }
        }

        public async void CWebBrowser_LoginCheck(object sender, FrameLoadEndEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                string htmlc = await e.Browser.MainFrame.GetTextAsync();
                if (htmlc.Contains("currentUser\": {"))
                {
                    Match ma = _emailRegex.Match(htmlc);
                    if (ma.Success)
                    {
                        var s = ma.Groups[1].Value;
                        if (!string.IsNullOrEmpty(GlobalData.VM_MA.Cookies))
                        {
                            if (s != GlobalData.VM_MA.Cookies)
                            {
                                GlobalData.VM_MA.Messages = $"登录的帐号与给定的邮箱不匹配{Environment.NewLine}已自动填入新邮箱";
                            }
                        }
                        GlobalData.VM_MA.Cookies = s;
                        GlobalData.VM_MA.IsInitialized = true;
                    }
                    else
                    {
                        GlobalData.VM_MA.Messages = $"无法从网页上获取邮箱{Environment.NewLine}请暂停使用Patreon并联系开发者";
                    }
                    _cwb.FrameLoadEnd -= CWebBrowser_LoginCheck;
                    _needLogin = false;
                    GlobalData.VM_MA.ShowLogin = false;
                }
            }
        }

        public async override Task<ResultMessage> GetArtistInfo(string url)
        {
            try
            {
                string ss = await GetWebCode(url);
                Match ma = _cidRegex.Match(ss);
                if (ma.Success)
                {
                    string _cid = ma.Groups[1].Value;
                    ss = await GetAPI(string.Format(_artistUrl, _cid));
                    var jpa = JsonConvert.DeserializeObject<JsonData_Patreon_Artist>(ss);
                    if (null != jpa.data)
                    {
                        var ai = new ArtistInfo()
                        {
                            Id = _cid,
                            Cid = _cid,
                            AName = GlobalData.RemoveLastDot(GlobalData.ReplacePath(jpa.data.attributes.name.Trim())),
                            PostUrl = url,
                            PayLow = GlobalData.VM_MA.Artist.PayLow,
                            PayHigh = GlobalData.VM_MA.Artist.PayHigh
                        };
                        return ResultHelper.NoError(ai);
                    }
                    return ResultHelper.IOError();
                }
                return ResultHelper.PathError();
            }
            catch (Exception ex)
            {
                GlobalData.ErrorLog(ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine + "-----------------------------------------------");
                return ResultHelper.UnKnownError();
            }
        }

        public async override Task<ResultMessage> GetArtistList()
        {
            try
            {
                string ss = await GetAPI(_pledgeUrl);
                var jpp = JsonConvert.DeserializeObject<JsonData_Patreon_Pledge>(ss);
                if (null != jpp.data && null != jpp.included)
                {
                    List<ArtistInfo> ais = new List<ArtistInfo>();
                    var tais = GlobalData.VM_MA.ArtistList.ToList();
                    if (tais.Count == 0)
                    {
                        tais.Add(new ArtistInfo());
                    }
                    var incll = jpp.included.ToList();
                    for (int i = 0; i < jpp.data.Length; i++)
                    {
                        var inclu = incll.Find(x => x.id == jpp.data[i].relationships.campaign.data.id);
                        var ai = new ArtistInfo()
                        {
                            Id = inclu.id,
                            Cid = inclu.id,
                            AName = GlobalData.RemoveLastDot(GlobalData.ReplacePath(inclu.attributes.name.Trim())),
                            PostUrl = inclu.attributes.url,
                            PayHigh = jpp.data[i].attributes.amount_cents.ToString()
                        };
                        tais.Remove(ai);
                        ais.Add(ai);
                    }
                    ais.AddRange(tais);
                    return ResultHelper.NoError(ais);
                }
                return ResultHelper.IOError();
            }
            catch (Exception ex)
            {
                GlobalData.ErrorLog(ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine + "-----------------------------------------------");
                return ResultHelper.UnKnownError();
            }
        }

        public override bool GetCover(BaseItem bi)
        {
            try
            {
                WebClient wc = new WebClient();
                wc.DownloadProgressChanged += Wc_DownloadProgressChanged;
                wc.DownloadDataCompleted += Wc_DownloadDataCompleted;
                wc.Proxy = GlobalData.VM_MA.MyProxy;
                wc.DownloadDataAsync(new Uri(bi.CoverPicThumb), bi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async override Task<ResultMessage> GetPostIDs(string uid)
        {
            try
            {
                string ss = ChangeUnicode(await GetAPI(string.Format(_postUrl, uid)));
                List<BaseItem> pis = new List<BaseItem>();
                while (true)
                {
                    var jpp = JsonConvert.DeserializeObject<JsonData_Patreon_Post>(ss);
                    if (null != jpp.data && null != jpp.included && null != jpp.meta)
                    {
                        var incll = jpp.included.ToList();
                        for (int i = 0; i < jpp.data.Length; i++)
                        {
                            if (DateTime.TryParse(jpp.data[i].attributes.published_at, out DateTime dt))
                            {
                                if (GlobalData.OverTime(dt))
                                {
                                    return ResultHelper.NoError(pis);
                                }
                            }

                            PatreonItem pi = new PatreonItem()
                            {
                                CreateDate = dt,
                                UpdateDate = dt,
                                PID = jpp.data[i].id,
                                Title = GlobalData.RemoveLastDot(GlobalData.ReplacePath(jpp.data[i].attributes.title.Trim())),
                                IsLiked = jpp.data[i].attributes.current_user_has_liked,
                                PLink = jpp.data[i].attributes.url
                            };
                            if (!string.IsNullOrEmpty(jpp.data[i].attributes.content))
                            {
                                pi.Comments.Add(await GetWebContent(_htmlImg.Replace(jpp.data[i].attributes.content, "")));
                            }
                            else if (!string.IsNullOrEmpty(jpp.data[i].attributes.content_teaser_text))
                            {
                                pi.Comments.Add(jpp.data[i].attributes.content_teaser_text);
                            }
                            if (null != jpp.data[i].attributes.image)
                            {
                                pi.CoverPicThumb = jpp.data[i].attributes.image.thumb_url;
                            }
                            if (null != jpp.data[i].attributes.embed)
                            {
                                pi.Comments.Add($"<引用链接: {jpp.data[i].attributes.embed.url} >");
                            }

                            if(null != jpp.data[i].relationships.media)
                            {
                                for (int j = 0; j < jpp.data[i].relationships.media.data.Length; j++)
                                {
                                    var inclu = incll.Find(x => x.id == jpp.data[i].relationships.media.data[j].id);
                                    if (inclu.attributes.file_name.StartsWith("https://"))
                                    {
                                        continue;
                                    }
                                    pi.ContentUrls.Add(inclu.attributes.image_urls.original);
                                    pi.FileNames.Add(inclu.attributes.file_name);
                                    pi.Comments.Add($"<文件: {inclu.attributes.file_name}>");
                                }
                            }
                            pis.Add(pi);
                        }
                        if (null != jpp.meta.pagination.cursors)
                        {
                            ss = await GetAPI(string.Format(_nextUrl, uid, jpp.meta.pagination.cursors.next));
                            continue;
                        }
                        return ResultHelper.NoError(pis);
                    }
                    return ResultHelper.IOError();
                }
            }
            catch (Exception ex)
            {
                GlobalData.ErrorLog(ex.Message + Environment.NewLine + ex.StackTrace + Environment.NewLine + "-----------------------------------------------");
                return ResultHelper.UnKnownError();
            }
        }

        public async override Task<ResultMessage> LikePost(string pid, string cid)
        {
            return await Task.Run(() => ResultHelper.UnKnownError("未支持的功能"));
        }

        private async Task<string> GetWebCode(string url)
        {
            _cwb.Load(url);
            do
            {
                await Task.Delay(100);
            } while (_cwb.IsLoading);
            return await _cwb.GetTextAsync();
        }

        private async Task<string> GetAPI(string url)
        {
            _cwb.Load(url);
            do
            {
                await Task.Delay(100);
            } while (_cwb.IsLoading);
            return await _cwb.GetTextAsync();
        }

        private async Task<string> GetWebContent(string content)
        {
            _cwb.LoadHtml(_webCharSet + content);
            do
            {
                await Task.Delay(150);
            } while (_cwb.IsLoading);
            return await _cwb.GetTextAsync();
        }
        
        private string ChangeUnicode(string str)
        {
            MatchCollection mc = Regex.Matches(str, "(\\\\u([\\w]{4}))");
            HashSet<string> _replaceFlag = new HashSet<string>();
            if (mc != null && mc.Count > 0)
            {
                foreach (Match m2 in mc)
                {
                    string v = m2.Value;
                    if (_unicodeDic.ContainsKey(v))
                    {
                        if (_replaceFlag.Add(v))
                        {
                            str = str.Replace(v, _unicodeDic[v]);
                        }
                    }
                    else
                    {
                        string word = v.Substring(2);
                        byte[] codes = new byte[2];
                        int code = Convert.ToInt32(word.Substring(0, 2), 16);
                        int code2 = Convert.ToInt32(word.Substring(2), 16);
                        codes[0] = (byte)code2;
                        codes[1] = (byte)code;
                        string ss = Encoding.Unicode.GetString(codes);
                        str = str.Replace(v, ss);
                        _unicodeDic.Add(v, ss);
                        _replaceFlag.Add(v);
                    }
                }
                return str;
            }
            else
            {
                return str;
            }
        }
    }
}
