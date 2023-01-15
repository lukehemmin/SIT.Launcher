﻿using MahApps.Metro.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using Octokit;
using SIT.Launcher.DeObfus;
using SIT.Launcher.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SIT.Launcher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            ExtractDeobfuscator();

            if (File.Exists("LauncherConfig.json"))
            {
                Config = JsonConvert.DeserializeObject<LauncherConfig>(File.ReadAllText("LauncherConfig.json"));
            }
            this.DataContext = this;

            this.Title = "SIT Launcher - " + App.ProductVersion.ToString();


            //if(Directory.Exists(AppContext.BaseDirectory + "/DeObfus/PatchedAssemblies/"))
            //{
            //    var patchedAssemblies = Directory.GetFiles(AppContext.BaseDirectory + "/DeObfus/PatchedAssemblies/");
            //    cbAssemblyDLLs.Items.Clear();
            //    cbAssemblyDLLs.Items.Add("Latest");
            //    foreach (var f in patchedAssemblies)
            //    {
            //        cbAssemblyDLLs.Items.Add(f);
            //    }
            //    cbAssemblyDLLs.SelectedIndex = 0;
            //}


        }

        public LauncherConfig Config { get; } = LauncherConfig.Instance;

        public IEnumerable<ServerInstance> ServerInstances 
        { 
            get 
            {
                return ServerInstance.ServerInstances.AsEnumerable();
            } 
        }


        //private string _serverAddress = "https://localhost:443";

        public enum ELaunchButtonState : short
        {
            Launch,
            Deob,
            BepInEx,
            Custom = short.MaxValue
        }
        public ELaunchButtonState LaunchButtonState { get; set; } = ELaunchButtonState.Launch;

        private string _launchButtonText = "Launch";

        public string LaunchButtonText 
        { 
            get 
            {
                switch (LaunchButtonState)
                {
                    case ELaunchButtonState.Launch:
                        _launchButtonText = "Launch";
                        break;
                    case ELaunchButtonState.Deob:
                        _launchButtonText = "Deobfuscating";
                        break;
                    case ELaunchButtonState.BepInEx:
                        _launchButtonText = "Installing BepInEx";
                        break;
                    case ELaunchButtonState.Custom:
                        break;
                    default:
                        _launchButtonText = LaunchButtonState.ToString();
                        break;
                }
                return _launchButtonText;
            }
            set
            {
                LaunchButtonState = ELaunchButtonState.Custom;
                _launchButtonText = value;
            }
        }

        public string Username
        {
            get
            {

                return Config.Username;
            }
        }

        public string ServerAddress { get {

                return Config.ServerInstance.ServerAddress;
            } }


        private void btnAddNewServer_Click(object sender, RoutedEventArgs e)
        {
            AddNewServerDialog addNewServerDialog = new AddNewServerDialog();
            addNewServerDialog.ShowDialog();
        }

        private void btnRemoveServer_Click(object sender, RoutedEventArgs e)
        {

        }

        private async void btnLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ServerAddress))
            {
                MessageBox.Show("No Server Address Provided");
                return;
            }
           
            if (ServerAddress.EndsWith("/"))
            {
                MessageBox.Show("Server Address is incorrect, you should NOT have a / at the end!");
                return;
            }
            TarkovRequesting requesting = new TarkovRequesting(null, ServerAddress, false);

            Dictionary<string, string> data = new Dictionary<string, string>();
            data.Add("username", Username);
            data.Add("email", Username);
            data.Add("edition", "Edge Of Darkness"); // default to EoD
            //data.Add("edition", "Standard");
            if (string.IsNullOrEmpty(txtPassword.Password))
            {
                MessageBox.Show("You cannot use an empty Password for your account!");
                return;
            }
            data.Add("password", txtPassword.Password);

            // connect and get editions
            //var returnDataConnect = requesting.PostJson("/launcher/server/connect", JsonConvert.SerializeObject(data));

            // attempt to login
            var returnData = requesting.PostJson("/launcher/profile/login", JsonConvert.SerializeObject(data));

            // If failed, attempt to register
            if (returnData == "FAILED")
            {
                var messageBoxResult = MessageBox.Show("Your account has not been found, would you like to register a new account with these credentials?", "Account", MessageBoxButton.YesNo);
                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    returnData = requesting.PostJson("/launcher/profile/register", JsonConvert.SerializeObject(data));
                }
                else
                {
                    return;
                }
            }

            // If all good, launch game with AID
            if(!string.IsNullOrEmpty(returnData) && returnData != "FAILED" && returnData != "ALREADY_IN_USE")
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Filter = "Executable (EscapeFromTarkov.exe)|EscapeFromTarkov.exe;";
                if(openFileDialog.ShowDialog() == true)
                {
                    var fvi = FileVersionInfo.GetVersionInfo(openFileDialog.FileName);
                    App.GameVersion = fvi.ProductVersion;

                    UpdateButtonText(null);

                    await DownloadAndInstallBepInEx5(openFileDialog.FileName);

                    await DownloadAndInstallSIT(openFileDialog.FileName);
                    
                    UpdateButtonText("Installing Aki");
                    await Task.Delay(1000);
                    // Copy Aki Dlls for support
                    DownloadAndInstallAki(openFileDialog.FileName);

                    // Deobfuscate Assembly-CSharp
                    if (Config.AutomaticallyDeobfuscateDlls 
                        && NeedsDeobfuscation(openFileDialog.FileName))
                    {
                        if(await Deobfuscate(openFileDialog.FileName))
                        {
                            StartGame(returnData, openFileDialog);
                        }
                    }
                    else
                    {
                        // Launch game
                        StartGame(returnData, openFileDialog);
                    }
                }
            }
            else if (returnData == "ALREADY_IN_USE")
            {
                var messageBoxResult = MessageBox.Show("The username/email has already been created, please use another one.", "Account");
            }
            else if (returnData.Length != 24) // NewId or something
            {
                var messageBoxResult = MessageBox.Show("Something went wrong. Maybe the server hasn't been started? Check the logs.", "Account");
            }
        }

        private async void StartGame(string sessionId, OpenFileDialog openFileDialog)
        {
            App.LegalityCheck();
            CleanupDirectory(openFileDialog);

            UpdateButtonText(null);
            btnLaunchGame.IsEnabled = true;
            var commandArgs = $"-token={sessionId} -config={{\"BackendUrl\":\"{ServerAddress}\",\"Version\":\"live\"}}";
            Process.Start(openFileDialog.FileName, commandArgs);
            Config.Save();
            WindowState = WindowState.Minimized;

            await Task.Delay(10000);

            if (Config.SendInfoToDiscord)
                DiscordInterop.DiscordRpcClient.UpdateDetails("In Game");
            //do
            //{

            //} while (Process.GetProcessesByName("EscapeFromTarkov") != null);
            if (Config.SendInfoToDiscord)
                DiscordInterop.DiscordRpcClient.UpdateDetails("");
        }

        private void CleanupDirectory(OpenFileDialog openFileDialog)
        {
            UpdateButtonText("Cleaning client directory");

            var battlEyeDirPath = Directory.GetParent(openFileDialog.FileName).FullName + "\\BattlEye";
            if (Directory.Exists(battlEyeDirPath))
            {
                Directory.Delete(battlEyeDirPath, true);
            }
            var battlEyeExePath = openFileDialog.FileName.Replace("EscapeFromTarkov", "EscapeFromTarkov_BE");
            if (File.Exists(battlEyeExePath))
            {
                File.Delete(battlEyeExePath);
            }
            var cacheDirPath = Directory.GetParent(openFileDialog.FileName).FullName + "\\cache";
            if (Directory.Exists(cacheDirPath))
            {
                Directory.Delete(cacheDirPath, true);
            }
            var consistancyInfoPath = openFileDialog.FileName.Replace("EscapeFromTarkov.exe", "ConsistencyInfo");
            if (File.Exists(consistancyInfoPath))
            {
                File.Delete(consistancyInfoPath);
            }
            var uninstallPath = openFileDialog.FileName.Replace("EscapeFromTarkov.exe", "Uninstall.exe");
            if (File.Exists(uninstallPath))
            {
                File.Delete(uninstallPath);
            }
        }

        private async Task DownloadAndInstallBepInEx5(string exeLocation)
        {
            UpdateButtonText("Installing BepInEx");
            await Task.Delay(1000);

            UpdateButtonText("Downloading BepInEx");
            btnLaunchGame.IsEnabled = false;
            await Task.Delay(1000);

            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (Directory.Exists(bepinexPluginsPath))
                return;

            if (!File.Exists(App.ApplicationDirectory + "\\BepInEx5.zip"))
            {
                var httpRequest = HttpWebRequest.Create("https://github.com/BepInEx/BepInEx/releases/download/v5.4.21/BepInEx_x64_5.4.21.0.zip");
                httpRequest.Method = "GET";
                var response = await httpRequest.GetResponseAsync();
                if (response != null)
                {
                    var ms = new MemoryStream();
                    var rStream = response.GetResponseStream();
                    rStream.CopyTo(ms);
                    await File.WriteAllBytesAsync(App.ApplicationDirectory + "\\BepInEx5.zip", ms.ToArray());
                }
            }

            System.IO.Compression.ZipFile.ExtractToDirectory(App.ApplicationDirectory + "\\BepInEx5.zip", baseGamePath);
            if (!Directory.Exists(bepinexPluginsPath))
            {
                Directory.CreateDirectory(bepinexPluginsPath);
            }

            UpdateButtonText(null);
            btnLaunchGame.IsEnabled = true;

        }

        private void UpdateButtonText(string text)
        {
            if (!string.IsNullOrEmpty(text)) {
                LaunchButtonText = text;
                LaunchButtonState = ELaunchButtonState.Custom;
            }
            else
            {
                LaunchButtonState = ELaunchButtonState.Launch;
            }

            btnLaunchGame.Content = LaunchButtonText;

            if(Config.SendInfoToDiscord)
                DiscordInterop.DiscordRpcClient.UpdateDetails(text);

        }

        private async Task DownloadAndInstallSIT(string exeLocation)
        {
            if (!Config.AutomaticallyInstallSIT)
                return;

            UpdateButtonText("Downloading SIT");
            btnLaunchGame.IsEnabled = false;

            var baseGamePath = Directory.GetParent(exeLocation).FullName;
            var bepinexPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            bepinexPath += "BepInEx";

            var bepinexPluginsPath = bepinexPath + "\\plugins\\";
            if (!Directory.Exists(bepinexPluginsPath))
                return;

            try
            {

                var github = new GitHubClient(new ProductHeaderValue("SIT-Launcher"));
                var user = await github.User.Get("paulov-t");
                var tarkovCoreReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Core");
                var latestCore = tarkovCoreReleases[0];
                //var tarkovCoreReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Tarkov.Core");
                //var latestCore = tarkovCoreReleases[0];
                //var tarkovSPReleases = await github.Repository.Release.GetAll("paulov-t", "SIT.Tarkov.SP");
                //var latestSP = tarkovSPReleases[0];
                var allAssets = latestCore.Assets.OrderByDescending(x => x.CreatedAt).DistinctBy(x => x.Name);
                var allAssetsCount = allAssets.Count();
                var assetIndex = 0;
                foreach (var A in allAssets)
                {
                    var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(A.BrowserDownloadUrl);
                    //var httpRequest = HttpWebRequest.Create(A.BrowserDownloadUrl);
                    //httpRequest.Method = "GET";
                    //var response = await httpRequest.GetResponseAsync();
                    if (response != null)
                    {
                        var ms = new MemoryStream();
                        //var rStream = response.GetResponseStream();
                        //rStream.CopyTo(ms);
                        await response.Content.CopyToAsync(ms);

                        var deliveryPath = App.ApplicationDirectory + "\\ClientMods\\" + A.Name;
                        var fiDelivery = new FileInfo(deliveryPath);
                        await File.WriteAllBytesAsync(deliveryPath, ms.ToArray());
                    }
                    UpdateButtonText($"Downloading SIT ({assetIndex}/{allAssetsCount})");
                    assetIndex++;
                }



                UpdateButtonText("Installing SIT");

                foreach (var clientModDLL in Directory.GetFiles(App.ApplicationDirectory + "\\ClientMods\\"))
                {
                    if (clientModDLL.Contains("Assembly-CSharp"))
                    {
                        var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
                        assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";

                        // Backup the Assembly-CSharp and place the newest clean one
                        if (!File.Exists(assemblyLocation + ".backup"))
                        {
                            File.Copy(assemblyLocation, assemblyLocation + ".backup");
                            File.Copy(clientModDLL, assemblyLocation, true);
                        }
                    }
                    else
                    {
                        bool shouldCopy = false;
                        var fiClientMod = new FileInfo(clientModDLL);
                        var fiExistingMod = new FileInfo(bepinexPluginsPath + "\\" + fiClientMod.Name);
                        if (fiExistingMod.Exists && allAssets.Any(x => x.Name == fiClientMod.Name))
                        {
                            var createdDateOfDownloadedAsset = allAssets.FirstOrDefault(x => x.Name == fiClientMod.Name).CreatedAt;
                            shouldCopy = (fiExistingMod.LastWriteTime < createdDateOfDownloadedAsset);
                        }
                        else
                            shouldCopy = true;

                        if (shouldCopy)
                            File.Copy(clientModDLL, bepinexPluginsPath + "\\" + fiClientMod.Name, true);
                    }
                }
            }
            catch (Exception ex)
            {
                var r = MessageBox.Show("Unable to download and install SIT", "Error");
            }



        }

        private void DownloadAndInstallAki(string exeLocation)
        {
            // Discover where Assembly-CSharp is within the Game Folders
            var managedPath = exeLocation.Replace("EscapeFromTarkov.exe", "");
            managedPath += "EscapeFromTarkov_Data\\Managed\\";
            DirectoryInfo diManaged = new DirectoryInfo(managedPath);
            if (diManaged.Exists)
            {
                List<FileInfo> fiAkiFiles = Directory.GetFiles(App.ApplicationDirectory + "/AkiSupport/").Select(x => new FileInfo(x)).ToList();
                foreach(var fileInfo in fiAkiFiles)
                {
                    fileInfo.CopyTo(managedPath + fileInfo.Name, true);
                }
            }
        }

        private void OnDeobfuscateLog(string s)
        {
            Dispatcher.Invoke(() =>
            {
                txtDeobfuscateLog.Text += s + Environment.NewLine;
            });
        }

        private async Task<bool> Deobfuscate(string exeLocation, bool createBackup = true, bool overwriteExisting = true, bool doRemapping = true)
        {
            Deobfuscator.Logged.Clear();
            await Dispatcher.InvokeAsync(() =>
            {
                txtDeobfuscateLog.Text = String.Empty;
            });
            //Deobfuscator.OnLog += OnDeobfuscateLog;
            Dispatcher.Invoke(() =>
            {
                txtDeobfuscateLog.Text = String.Empty;
                OnDeobfuscateLog("--------------------------------------------------------------------------");
                OnDeobfuscateLog("Deobfuscate started!" + Environment.NewLine);
                btnDeobfuscate.IsEnabled = false;
            });
            var result = await Deobfuscator.DeobfuscateAsync(exeLocation, createBackup, overwriteExisting, doRemapping);
            Dispatcher.Invoke(() =>
            {
                foreach (var logg in Deobfuscator.Logged)
                {
                    txtDeobfuscateLog.Text += logg + Environment.NewLine;
                }
                btnDeobfuscate.IsEnabled = true;
            });

            var deobfuscateLogPath = "DeobfuscateLog.txt";
            if (File.Exists(deobfuscateLogPath))
                File.Delete(deobfuscateLogPath);    

            await File.WriteAllTextAsync(deobfuscateLogPath, txtDeobfuscateLog.Text);
            //Deobfuscator.OnLog -= OnDeobfuscateLog;
            return result;
        }

        private bool NeedsDeobfuscation(string exeLocation)
        {
            var assemblyLocation = exeLocation.Replace("EscapeFromTarkov.exe", "");
            assemblyLocation += "EscapeFromTarkov_Data\\Managed\\Assembly-CSharp.dll";
            return !File.Exists(assemblyLocation + ".backup");
        }

        private void ExtractDeobfuscator()
        {
            var deobfusFolder = App.ApplicationDirectory + "/DeObfus/";
            if (!File.Exists(deobfusFolder + "Deobfuscator.zip"))
                return;

            if (!File.Exists(deobfusFolder + "/de4dot/" + "de4dot.exe"))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(deobfusFolder + "Deobfuscator.zip", deobfusFolder + "/de4dot/");
                File.Delete(deobfusFolder + "Deobfuscator.zip");
            }
        }

       

        private void btnStartServer_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Executable (Server.exe)|Server.exe;";
            if (openFileDialog.ShowDialog() == true)
            {
                //if(!Process.GetProcessesByName("Server").Any())
                    Process.Start(openFileDialog.FileName, "");
            }
        }

        private void CollapseAll()
        {
            gridPlay.Visibility = Visibility.Collapsed;
            gridCoopServer.Visibility = Visibility.Collapsed;
            gridTools.Visibility = Visibility.Collapsed;
            gridSettings.Visibility = Visibility.Collapsed;
        }

        private void btnCoopServer_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridCoopServer.Visibility = Visibility.Visible;
        }

        private void btnPlay_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridPlay.Visibility = Visibility.Visible;
        }

        private void btnSettingsPopup_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridSettings.Visibility = Visibility.Visible;
        }

        private void btnToToolsWindow_Click(object sender, RoutedEventArgs e)
        {
            CollapseAll();
            gridTools.Visibility = Visibility.Visible;
        }

        private async void btnDeobfuscate_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Executable (EscapeFromTarkov.exe)|EscapeFromTarkov.exe;";
            if (openFileDialog.ShowDialog() == true)
            {
                await Deobfuscate(openFileDialog.FileName, doRemapping: true);
            }
        }

        private void btnDeobfuscateBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "DLL (Assembly-CSharp)|Assembly-CSharp*.dll;";
            if (openFileDialog.ShowDialog() == true)
            {
                Deobfuscator.DeobfuscateAssembly(openFileDialog.FileName, Directory.GetParent(openFileDialog.FileName).FullName, doRemapping: true);
            }
        }
    }
}
