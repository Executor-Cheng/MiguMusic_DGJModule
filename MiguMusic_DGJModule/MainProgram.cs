using BilibiliDM_PluginFramework;
using DGJv3;
using MiguMusic_DGJModule.MiguMusic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using SongInfo = DGJv3.SongInfo;

namespace MiguMusic_DGJModule
{
    public class PluginMain : DMPlugin
    {
        public static MiguModule MiguModule { get; private set; }

        public PluginMain()
        {
            this.PluginName = "咪咕喵块";
            try { this.PluginAuth = BiliUtils.GetUserNameByUserId(35744708); }
            catch { this.PluginAuth = "西井丶"; }
            this.PluginCont = "847529602@qq.com";
            this.PluginDesc = "使用咪咕音乐平台进行点歌~";
            this.PluginVer = MiguMusicApi.Version;
            base.Start();
        }

        public override void Inited()
        {
            try
            {
                MiguModule = new MiguModule();
                InjectDGJ();
            }
            catch (Exception Ex)
            {
                MessageBox.Show($"插件初始化失败了喵,请将桌面上的错误报告发送给作者（/TДT)/\n{Ex.ToString()}", "咪咕喵块", 0, MessageBoxImage.Error);
                throw;
            }
            VersionChecker vc = new VersionChecker("MiguMusic_DGJModule");
            if (!vc.FetchInfo())
            {
                Log($"版本检查失败了喵 : {vc.lastException.Message}");
                return;
            }
            if (vc.hasNewVersion(this.PluginVer))
            {
                Log($"有新版本了喵~最新版本 : {vc.Version}\n                {vc.UpdateDescription}");
                Log($"下载地址 : {vc.DownloadUrl}");
                Log($"插件页面 : {vc.WebPageUrl}");
            }
        }

        public override void Start()
        {
            Log("若要启用插件,去点歌姬内把“咪咕音乐”选入首/备选模块之一即可喵");
        }

        public override void Stop()
        {
            Log("若要禁用插件,去点歌姬内把“咪咕音乐”移出首/备选模块即可喵");
        }

        private void InjectDGJ()
        {
            try
            {
                Assembly dgjAssembly = Assembly.GetAssembly(typeof(SearchModule)); //如果没有点歌姬插件，插件的构造方法会抛出异常，无需考虑这里的assembly == null的情况
                Assembly dmAssembly = Assembly.GetEntryAssembly();
                Type appType = dmAssembly.ExportedTypes.FirstOrDefault(p => p.FullName == "Bililive_dm.App");
                ObservableCollection<DMPlugin> Plugins = (ObservableCollection<DMPlugin>)appType.GetField("Plugins", BindingFlags.GetField | BindingFlags.Static | BindingFlags.Public).GetValue(null);
                DMPlugin dgjPlugin = Plugins.FirstOrDefault(p => p.ToString() == "DGJv3.DGJMain");
                object dgjWindow = null;
                try
                {
                    dgjWindow = dgjAssembly.DefinedTypes.FirstOrDefault(p => p.Name == "DGJMain").GetField("window", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dgjPlugin);
                }
                catch (ReflectionTypeLoadException Ex) // 缺少登录中心时
                {
                    dgjWindow = Ex.Types.FirstOrDefault(p => p.Name == "DGJMain").GetField("window", BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dgjPlugin);
                }
                object searchModules = dgjWindow.GetType().GetProperty("SearchModules", BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public).GetValue(dgjWindow);
                ObservableCollection<SearchModule> searchModules2 = (ObservableCollection<SearchModule>)searchModules.GetType().GetProperty("Modules", BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public).GetValue(searchModules);
                SearchModule nullModule = (SearchModule)searchModules.GetType().GetProperty("NullModule", BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance).GetValue(searchModules);
                SearchModule lwlModule = searchModules2.FirstOrDefault(p => p != nullModule);
                if (lwlModule != null)
                {
                    Action<string> logHandler = (Action<string>)lwlModule.GetType().GetProperty("_log", BindingFlags.GetProperty | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lwlModule);
                    MiguModule.SetLogHandler(logHandler);
                }
                searchModules2.Insert(3, MiguModule);
            }
            catch (Exception Ex)
            {
                MessageBox.Show($"注入到点歌姬失败了喵\n{Ex.ToString()}", "咪咕喵块", 0, MessageBoxImage.Error);
                throw;
            }
        }
    }

    public class MiguModule : SearchModule
    {
        static MiguModule()
        {
            string assemblyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"弹幕姬\plugins\Assembly");
            if (!Directory.Exists(assemblyPath))
            {
                Directory.CreateDirectory(assemblyPath);
            }
            string filePath = Path.Combine(assemblyPath, "HtmlAgilityPack.dll");
            if (!File.Exists(filePath))
            {
                File.WriteAllBytes(filePath, Properties.Resources.HtmlAgilityPack);
            }
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            string dllName = args.Name.Split(',')[0];
            if (dllName == "HtmlAgilityPack")
            {
                string assemblyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), @"弹幕姬\plugins\Assembly");
                return Assembly.LoadFrom(Path.Combine(assemblyPath, "HtmlAgilityPack.dll"));
            }
            else
            {
                return null;
            }
        }

        private IDictionary<string, LyricInfo> LyricCache { get; } = new Dictionary<string, LyricInfo>();

        public MiguModule()
        {
            string authorName;
            try { authorName = BiliUtils.GetUserNameByUserId(35744708); }
            catch { authorName = "西井丶"; }
            SetInfo("咪咕音乐", authorName, "847529602@qq.com", MiguMusicApi.Version, "使用咪咕音乐平台进行点歌~");
            this.GetType().GetProperty("IsPlaylistSupported", BindingFlags.SetProperty | BindingFlags.Public | BindingFlags.Instance).SetValue(this, true); // Enable Playlist Supporting
        }

        public void SetLogHandler(Action<string> logHandler)
        {
            this.GetType().GetProperty("_log", BindingFlags.SetProperty | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(this, logHandler);
        }

        protected override DownloadStatus Download(SongItem item)
        {
            throw new NotImplementedException();
        }

        protected override string GetDownloadUrl(SongItem songInfo)
        {
            return MiguMusicApi.GetSongUrl(songInfo.SongId);
        }

        [Obsolete("Use GetLyricById instead", true)]
        protected override string GetLyric(SongItem songInfo)
        {
            throw new NotImplementedException();
        }

        protected override string GetLyricById(string copyrightId)
        {
            LyricInfo lyric = null;
            try
            {
                lyric = _GetLyric(copyrightId);
            }
            catch (Exception Ex)
            {
                Log($"获取歌词失败了喵:{Ex.Message}");
            }
            return lyric?.GetLyricText();
        }

        protected override List<SongInfo> GetPlaylist(string keyword)
        {
            MiguMusic.SongInfo[] songs;
            if (long.TryParse(keyword, out long id))
            {
                songs = MiguMusicApi.GetPlaylist(id);
                return songs.Select(p => new SongInfo(this, p.CopyrightId, p.Name, new string[] { p.Artist }, null)).ToList();
            }
            else
            {
                Log("提供的Id必须为“http://music.migu.cn/v3/music/playlist/”后边的数字喵！");
                return new List<SongInfo>();
            }
        }

        protected override SongInfo Search(string keyword)
        {
            try
            {
                MiguMusic.SongInfo song = MiguMusicApi.SearchSong(keyword);
                LyricInfo lyric = null;
                try
                {
                    lyric = _GetLyric(song.CopyrightId);
                }
                catch (Exception Ex)
                {
                    Log($"获取歌词失败了喵:{Ex.Message}");
                }
                return new SongInfo(this, song.CopyrightId, song.Name, new string[] { song.Artist }, lyric?.GetLyricText());
            }
            catch (Exception Ex)
            {
                Log($"搜索单曲失败了喵:{Ex.Message}");
            }
            return null;
        }

        private LyricInfo _GetLyric(string copyrightId, bool useCache = true)
        {
            if (!useCache || !LyricCache.ContainsKey(copyrightId))
            {
                LyricInfo lyric = MiguMusicApi.GetLyric(copyrightId);
                LyricCache[copyrightId] = lyric;
            }
            return LyricCache[copyrightId];
        }
    }
}
