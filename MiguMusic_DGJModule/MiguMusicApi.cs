using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace MiguMusic_DGJModule.MiguMusic
{
    public static class MiguMusicApi
    {
        public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        public static string DefaultUserAgent { get; } = $"DGJModule.MiguMusicApi/{Version} .NET CLR v4.0.30319";

        public static IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>
        {
            ["Origin"] = "http://music.migu.cn/",
            ["Referer"] = "http://music.migu.cn/"
        };

        public static class CryptoHelper
        {
            public static byte[] SaltedStringBuffer { get; } = Encoding.ASCII.GetBytes("Salted__");

            public struct MiguCryptoKeys
            {
                public byte[] Password;
                public byte[] Salt;
                public byte[] Key;
                public byte[] IV;
            }

            public static RSACryptoServiceProvider RsaEncoder { get; } = RsaHelper.DecodeRSAPublicKey(Convert.FromBase64String("MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQC8asrfSaoOb4je+DSmKdriQJKW\nVJ2oDZrs3wi5W67m3LwTB9QVR+cE3XWU21Nx+YBxS0yun8wDcjgQvYt625ZCcgin\n2ro/eOkNyUOTBIbuj9CvMnhUYiR61lC1f1IGbrSYYimqBVSjpifVufxtx/I3exRe\nZosTByYp4Xwpb1+WAQIDAQAB"));

            public static MiguCryptoKeys CreateMiguCryptoKeys()
            {
                Random r = new Random();
                byte[] password = new byte[32], salt = new byte[8];
                r.NextBytes(password);
                r.NextBytes(salt);
                string passwordHex = string.Join("", password.Select(p => p.ToString("x2")));
                password = Encoding.UTF8.GetBytes(passwordHex);
                byte[] result = new byte[0];
                byte[] previousResult = new byte[0];
                for (int i = 0; i < 5; i++)
                {
                    using (MD5 md5 = new MD5CryptoServiceProvider())
                    {
                        previousResult = md5.ComputeHash(previousResult.Concat(password).Concat(salt).ToArray());
                        result = result.Concat(previousResult).ToArray();
                    }
                }
                return new MiguCryptoKeys
                {
                    Password = password,
                    Salt = salt,
                    Key = result.Take(32).ToArray(),
                    IV = result.Skip(32).Take(16).ToArray()
                };
            }

            public static byte[] AesEncrypt(byte[] toEncrypt, CipherMode mode, byte[] key, byte[] iv)
            {
                using (AesCryptoServiceProvider aes = new AesCryptoServiceProvider())
                {
                    aes.Mode = mode;
                    aes.Key = key;
                    aes.IV = iv;
                    using (MemoryStream ms = new MemoryStream())
                    using (CryptoStream cStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cStream.Write(toEncrypt, 0, toEncrypt.Length);
                        cStream.FlushFinalBlock();
                        return ms.ToArray();
                    }
                }
            }

            public static byte[] AesEncrypt(string toEncrypt, CipherMode mode, byte[] key, byte[] iv)
                => AesEncrypt(Encoding.UTF8.GetBytes(toEncrypt), mode, key, iv);

            public static byte[] RsaEncrypt(byte[] toEncrypt)
                => RsaEncoder.Encrypt(toEncrypt, false);

            public static byte[] MD5Encrypt(byte[] toEncrypt)
            {
                using (MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider())
                {
                    return md5.ComputeHash(toEncrypt);
                }
            }
        }

        public static string GetQueryString(string body)
        {
            CryptoHelper.MiguCryptoKeys keys = CryptoHelper.CreateMiguCryptoKeys();
            byte[] aesEncrypted = CryptoHelper.AesEncrypt(Encoding.UTF8.GetBytes(body), CipherMode.CBC, keys.Key, keys.IV);
            byte[] rsaEncrypted = CryptoHelper.RsaEncrypt(keys.Password);
            return $"dataType=2&data={WebUtility.UrlEncode(Convert.ToBase64String(Encoding.UTF8.GetBytes("Salted__").Concat(keys.Salt).Concat(aesEncrypted).ToArray()))}&secKey={WebUtility.UrlEncode(Convert.ToBase64String(rsaEncrypted))}";
        }

        public static string GetSongUrl(string copyrightId)
        {
            string payload = new JObject { ["copyrightId"] = copyrightId }.ToString(0);
            string json = HttpHelper.HttpGet($"http://music.migu.cn/v3/api/music/audioPlayer/getPlayInfo?{GetQueryString(payload)}", headers: DefaultHeaders);
            try
            {
                JObject j = JObject.Parse(json);
                if (j["returnCode"].ToObject<int>() == 0)
                {
                    string playUrl = (j["data"]["hqPlayInfo"] ?? j["data"]["bqPlayInfo"] ?? throw new NotSupportedException("无法获取歌曲下载链接"))["playUrl"].ToString();
                    return !string.IsNullOrEmpty(playUrl) ? playUrl : throw new NotSupportedException("无法获取歌曲下载链接");
                }
                else
                {
                    throw new NotImplementedException("意外的服务器返回");
                }
            }
            catch (JsonReaderException)
            {
                throw new NotImplementedException("意外的服务器返回");
            }
        }

        public static SongInfo[] GetPlaylist(long id)
        {
            string html = HttpHelper.HttpGet($"http://music.migu.cn/v3/music/playlist/{id}", headers: DefaultHeaders);
            try
            {
                HtmlDocument document = new HtmlDocument();
                document.LoadHtml(html);
                HtmlNode root = document.DocumentNode;
                HtmlNodeCollection list = root.SelectNodes("//div[@class='row J_CopySong']");
                return list.Where(p => p.Attributes.Any(q => q.Name == "data-cid" && !string.IsNullOrEmpty(q.Value))).Select(p => new SongInfo
                {
                    CopyrightId = p.Attributes["data-cid"].Value,
                    Name = p.SelectSingleNode(".//a[contains(@class,'song-name-txt')]")?.InnerText,
                    Artist = string.Join(",", p.SelectSingleNode("./div[contains(@class,'song-singers')]").SelectNodes("./a")?.Select(q => q.InnerText) ?? Array.Empty<string>()),
                    Album = p.SelectSingleNode("./div[contains(@class,'song-belongs')]").SelectSingleNode("./a")?.InnerText,
                    AlbumId = int.Parse(p.SelectSingleNode("./div[contains(@class,'song-belongs')]").SelectSingleNode("./a")?.Attributes["href"].Value.Split('/').Last() ?? "0")

                }).ToArray();
            }
            catch (JsonReaderException)
            {
                throw new NotImplementedException("意外的服务器返回");
            }
        }

        public static SongInfo SearchSong(string keyword)
        {
            string json = HttpHelper.HttpGet($"http://m.music.migu.cn/migu/remoting/scr_search_tag?keyword={WebUtility.UrlEncode(keyword)}&type=2&rows=20&pgc=1", headers: DefaultHeaders);
            JObject j = JObject.Parse(json);
            if (j["success"].ToObject<bool>())
            {
                return j["musics"].Any() ? j["musics"][0].ToObject<SongInfo>() : throw new ArgumentException("未搜索到结果");
            }
            else
            {
                throw new NotImplementedException("意外的服务器返回");
            }
        }

        public static LyricInfo GetLyric(string copyrightId)
        {
            string json = HttpHelper.HttpGet($"http://music.migu.cn/v3/api/music/audioPlayer/getLyric?copyrightId={copyrightId}", headers: DefaultHeaders);
            try
            {
                JObject j = JObject.Parse(json);
                if (j["returnCode"].ToObject<int>() == 0)
                {
                    string lyric = j["lyric"].ToString();
                    return new LyricInfo(lyric);
                }
                else
                {
                    throw new NotImplementedException("意外的服务器返回");
                }
            }
            catch (JsonReaderException)
            {
                throw new NotImplementedException("意外的服务器返回");
            }
        }
    }

    public class SongInfo
    {
        [JsonProperty("albumName")]
        public string Album { get; set; }
        [JsonProperty("albumId")]
        public int AlbumId { get; set; }
        [JsonProperty("copyrightId")]
        public string CopyrightId { get; set; }
        [JsonProperty("lyrics")]
        public string LyricUrl { get; set; }
        [JsonProperty("songName")]
        public string Name { get; set; }
        [JsonProperty("artist")]
        public string Artist { get; set; }
        [JsonProperty("hasHQqq")]
        public string _HasHighQuality { set => HasHighQuality = value == "1"; }
        [JsonProperty("hasSQqq")]
        public string _HasSuperQuality { set => HasSuperQuality = value == "1"; }
        public bool HasHighQuality { get; set; }
        public bool HasSuperQuality { get; set; }
    }

    public class LyricInfo
    {
        public string Title { get; private set; }

        public string Artist { get; private set; }

        public string Album { get; private set; }

        public string LrcBy { get; private set; }

        public int Offset { get; private set; }

        public IDictionary<double, string> LrcWord { get; }

        public LyricInfo()
        {
            LrcWord = new SortedDictionary<double, string>();
        }

        public LyricInfo(string lyricText) : this()
            => AppendLrc(lyricText);

        public int GetCurrentLyric(double seconds, out string current, out string upcoming)
        {
            if (LrcWord.Count < 1)
            {
                current = "无歌词";
                upcoming = string.Empty;
                return -1;
            }
            List<KeyValuePair<double, string>> list = LrcWord.ToList();
            int i;
            if (seconds < list[0].Key)
            {
                i = 0;
                current = string.Empty;
                upcoming = list[0].Value;
            }
            else
            {
                for (i = 1; i < LrcWord.Count && !(seconds < list[i].Key); i++)
                {
                }
                current = list[i - 1].Value;
                if (list.Count > i)
                {
                    upcoming = list[i].Value;
                }
                else
                {
                    upcoming = string.Empty;
                }
            }
            return i;
        }

        public string GetLyricText()
        {
            StringBuilder lyric = new StringBuilder();
            if (!string.IsNullOrEmpty(Title))
            {
                lyric.AppendLine($"[ti:{Title}]");
            }
            if (!string.IsNullOrEmpty(Artist))
            {
                lyric.AppendLine($"[ar:{Artist}]");
            }
            if (!string.IsNullOrEmpty(Album))
            {
                lyric.AppendLine($"[al:{Album}]");
            }
            if (!string.IsNullOrEmpty(LrcBy))
            {
                lyric.AppendLine($"[by:{LrcBy}]");
            }
            if (Offset != 0)
            {
                lyric.AppendLine($"[offset:{Offset}]");
            }
            lyric.Append(string.Join(Environment.NewLine, LrcWord.GroupBy(p => p.Value).Select(p => $"{string.Join("", p.Select(q => $"[{TimeSpan.FromSeconds(q.Key).ToString(@"mm\:ss\.ff")}]"))}{p.Key}")));
            return lyric.ToString();
        }

        public void AppendLrc(string lrcText)
        {
            string[] array = lrcText.Split(new string[2]
            {
                "\r\n",
                "\n"
            }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string text in array)
            {
                if (text.StartsWith("[ti:"))
                {
                    Title = SplitInfo(text);
                }
                else if (text.StartsWith("[ar:"))
                {
                    Artist = SplitInfo(text);
                }
                else if (text.StartsWith("[al:"))
                {
                    Album = SplitInfo(text);
                }
                else if (text.StartsWith("[by:"))
                {
                    LrcBy = SplitInfo(text);
                }
                else if (text.StartsWith("[offset:"))
                {
                    Offset = int.Parse(SplitInfo(text));
                }
                else
                {
                    try
                    {
                        string value = new Regex(".*\\](.*)").Match(text).Groups[1].Value;
                        if (!(value.Replace(" ", "") == ""))
                        {
                            foreach (Match item in new Regex("\\[([0-9.:]*)\\]", RegexOptions.Compiled).Matches(text))
                            {
                                double totalSeconds = TimeSpan.Parse("00:" + item.Groups[1].Value).TotalSeconds + Offset / 1000;
                                if (LrcWord.ContainsKey(totalSeconds))
                                {
                                    LrcWord[totalSeconds] += $"({value})";
                                }
                                else
                                {
                                    LrcWord[totalSeconds] = value;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string SplitInfo(string line)
        {
            return line.Substring(line.IndexOf(":") + 1).TrimEnd(']');
        }
    }
}
