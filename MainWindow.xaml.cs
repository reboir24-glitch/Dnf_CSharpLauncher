using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using MySqlConnector;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace DFOLauncher
{
    public partial class MainWindow : Window
    {
        private string DB_HOST = "192.168.200.131";
        private string DB_USER = "game";
        private string DB_PASS = "uu5!^%jg";
        private int DB_PORT = 3306;

        private readonly string _configPath = "config.json";
        private readonly string _dbConfigPath = "dbconfig.json";
        private Config _config;
        private LoginSession _currentSession;
        private DispatcherTimer _serverCheckTimer;
        private string _gamePath = "DNF.exe";

        public MainWindow()
        {
            InitializeComponent();
            LoadDbConfig();
            LoadConfig();
            StartServerStatusCheck();

            // Start with fade in animation
            this.Opacity = 0;
            this.Loaded += (s, e) =>
            {
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                this.BeginAnimation(OpacityProperty, fadeIn);
            };
        }

        private void StartServerStatusCheck()
        {
            // Check server status immediately
            CheckServerStatus();

            // Set up timer to check every 30 seconds
            _serverCheckTimer = new DispatcherTimer();
            _serverCheckTimer.Interval = TimeSpan.FromSeconds(30);
            _serverCheckTimer.Tick += (s, e) => CheckServerStatus();
            _serverCheckTimer.Start();
        }

        private async void CheckServerStatus()
        {
            try
            {
                var ping = new Ping();
                var reply = await Task.Run(() => ping.Send(DB_HOST, 3000));

                Dispatcher.Invoke(() =>
                {
                    if (reply.Status == IPStatus.Success)
                    {
                        ServerDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                        ServerGlow.Color = (Color)ColorConverter.ConvertFromString("#10B981");
                        ServerStatus.Text = "Online";
                        ServerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                        PingText.Text = $"{reply.RoundtripTime}ms";
                        PingText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    }
                    else
                    {
                        SetServerOffline();
                    }
                });
            }
            catch
            {
                Dispatcher.Invoke(() => SetServerOffline());
            }
        }

        private void SetServerOffline()
        {
            ServerDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            ServerGlow.Color = (Color)ColorConverter.ConvertFromString("#EF4444");
            ServerStatus.Text = "Offline";
            ServerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            PingText.Text = "";
        }

        #region Window Controls
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            _serverCheckTimer?.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, _) => Close();
            this.BeginAnimation(OpacityProperty, fadeOut);
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            GamePathBox.Text = _gamePath;
            ServerBox.Text = DB_HOST;
            SettingsOverlay.Visibility = Visibility.Visible;
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Game Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                GamePathBox.Text = dialog.FileName;
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _gamePath = GamePathBox.Text;
            DB_HOST = ServerBox.Text;

            _config.GamePath = _gamePath;
            _config.Server = DB_HOST;
            SaveConfig();

            // Re-check server status with new IP
            CheckServerStatus();

            SettingsOverlay.Visibility = Visibility.Collapsed;
            SetStatus("Settings saved!", (Brush)FindResource("SuccessBrush"));
        }
        #endregion

        #region Config
        private void LoadDbConfig()
        {
            try
            {
                if (File.Exists(_dbConfigPath))
                {
                    var json = File.ReadAllText(_dbConfigPath);
                    var dbConfig = JsonConvert.DeserializeObject<DbConfig>(json);
                    if (dbConfig != null)
                    {
                        if (!string.IsNullOrEmpty(dbConfig.DatabaseHost))
                            DB_HOST = dbConfig.DatabaseHost;
                        if (!string.IsNullOrEmpty(dbConfig.DatabaseUser))
                            DB_USER = dbConfig.DatabaseUser;
                        if (!string.IsNullOrEmpty(dbConfig.DatabasePassword))
                            DB_PASS = dbConfig.DatabasePassword;
                        if (dbConfig.DatabasePort > 0)
                            DB_PORT = dbConfig.DatabasePort;
                    }
                }
            }
            catch
            {
                // Use default values if config fails to load
            }
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                    UsernameBox.Text = _config.Username;
                    PasswordBox.Password = _config.Password;
                    RememberCheckBox.IsChecked = _config.Remember;

                    if (!string.IsNullOrEmpty(_config.GamePath))
                        _gamePath = _config.GamePath;
                    if (!string.IsNullOrEmpty(_config.Server))
                        DB_HOST = _config.Server;
                }
                else
                {
                    _config = new Config();
                }
            }
            catch
            {
                _config = new Config();
            }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }
        #endregion

        #region UI Helpers
        private void ShowLoading(string message)
        {
            LoadingText.Text = message;
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void HideLoading()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void SetStatus(string message, Brush color)
        {
            StatusText.Text = message;
            StatusText.Foreground = color;
            StatusDot.Fill = color;
        }

        private void SwitchToLogin()
        {
            DashboardScreen.Visibility = Visibility.Collapsed;
            LoginScreen.Visibility = Visibility.Visible;
            AnimateSlideUp(LoginScreen);
        }

        private void SwitchToDashboard()
        {
            LoginScreen.Visibility = Visibility.Collapsed;
            DashboardScreen.Visibility = Visibility.Visible;
            AnimateSlideUp(DashboardScreen);
        }

        private void AnimateSlideUp(FrameworkElement element)
        {
            var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
            element.RenderTransform = transform;

            var slideAnim = new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));

            transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            element.BeginAnimation(OpacityProperty, fadeAnim);
        }
        #endregion

        #region Login/Register
        private async void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter username and password.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShowLoading("Signing in...");

            try
            {
                var session = await Task.Run(() => PerformLogin(username, password));
                _currentSession = session;

                if (RememberCheckBox.IsChecked == true)
                {
                    _config.Username = username;
                    _config.Password = password;
                    _config.Remember = true;
                    SaveConfig();
                }

                UpdateDashboard();
                SwitchToDashboard();
                SetStatus("✓ Login successful!", (Brush)FindResource("SuccessBrush"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Login Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                HideLoading();
            }
        }

        private void RegisterLink_Click(object sender, MouseButtonEventArgs e)
        {
            RegisterBtn_Click(sender, null);
        }

        private async void RegisterBtn_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter username and password.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShowLoading("Creating account...");

            try
            {
                await Task.Run(() => CreateAccount(username, password));
                MessageBox.Show("Account created successfully! You can now sign in.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                SetStatus("Account created successfully!", (Brush)FindResource("SuccessBrush"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Registration Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                HideLoading();
            }
        }

        private LoginSession PerformLogin(string username, string password)
        {
            var mainConnStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=d_taiwan;Uid={DB_USER};Pwd={DB_PASS};";
            int uid;
            string storedHash;

            using (var conn = new MySqlConnection(mainConnStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT uid, password FROM accounts WHERE accountname = @user", conn))
                {
                    cmd.Parameters.AddWithValue("@user", username);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (!reader.Read())
                            throw new Exception("User not found.");

                        uid = reader.GetInt32("uid");
                        var passField = reader["password"];
                        if (passField is byte[] passBytes)
                            storedHash = Encoding.UTF8.GetString(passBytes);
                        else
                            storedHash = passField.ToString();
                    }
                }
            }

            var inputHash = ComputeMd5(password);
            if (inputHash != storedHash)
                throw new Exception("Invalid password.");

            // Get CERA
            long cera = 0;
            var billingConnStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_billing;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(billingConnStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("SELECT cera FROM cash_cera WHERE account = @uid", conn))
                {
                    cmd.Parameters.AddWithValue("@uid", uid);
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        cera = Convert.ToInt64(result);
                }
            }

            // Get Characters
            var characters = new List<Character>();
            var charConnStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain;Uid={DB_USER};Pwd={DB_PASS};";

            using (var conn = new MySqlConnection(charConnStr))
            {
                conn.Open();

                var sql = @"
SELECT 
    c.charac_no,
    c.charac_name,
    c.lev,
    c.job,
    c.maxHP,
    c.maxMP,
    c.phy_attack,
    c.mag_attack,

    -- max fatigue from charac_info
    (c.max_fatigue + IFNULL(c.max_premium_fatigue, 0)) AS total_max_fatigue,

    -- used fatigue ONLY from charac_stat
    (IFNULL(s.used_fatigue, 0) + IFNULL(s.premium_fatigue, 0)) AS used_fatigue,

    -- last play time from charac_stat
    s.last_play_time,

    i.money
FROM charac_info c
LEFT JOIN taiwan_cain_2nd.inventory i
    ON c.charac_no = i.charac_no
LEFT JOIN charac_stat s
    ON c.charac_no = s.charac_no
WHERE c.m_id = @uid
  AND c.delete_flag = 0";

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", uid);

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            short maxFatigue = reader.GetInt16("total_max_fatigue");
                            short usedFatigue = reader.GetInt16("used_fatigue");

                            characters.Add(new Character
                            {
                                Id = reader.GetInt32("charac_no"),
                                Name = reader.GetString("charac_name"),
                                Level = reader.GetInt32("lev"),
                                Job = GetJobName(reader.GetInt32("job")),
                                Money = reader.IsDBNull(reader.GetOrdinal("money")) ? 0 : reader.GetInt64("money"),
                                MaxHP = reader.GetInt32("maxHP"),
                                MaxMP = reader.GetInt32("maxMP"),
                                PhyAttack = reader.GetInt32("phy_attack"),
                                MagAttack = reader.GetInt32("mag_attack"),

                                MaxFatigue = maxFatigue,
                                Fatigue = (short)Math.Max(0, maxFatigue - usedFatigue),

                                LastPlayTime = reader.IsDBNull(reader.GetOrdinal("last_play_time"))
                                    ? DateTime.MinValue
                                    : reader.GetDateTime("last_play_time")
                            });
                        }
                    }
                }
            }



            var token = GenerateLoginToken(uid);

            return new LoginSession
            {
                Uid = uid,
                Token = token,
                Characters = characters,
                Cera = cera
            };
        }

        private void CreateAccount(string username, string password)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=d_taiwan;Uid={DB_USER};Pwd={DB_PASS};";
            var hashedPassword = ComputeMd5(password);
            int uid = 0;

            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();

                // FIX: prevent duplicate usernames without DB changes
                using (var checkCmd = new MySqlCommand(
                    "SELECT 1 FROM accounts WHERE accountname = @user LIMIT 1", conn))
                {
                    checkCmd.Parameters.AddWithValue("@user", username);
                    if (checkCmd.ExecuteScalar() != null)
                        throw new Exception("Account name already exists!");
                }

                using (var tx = conn.BeginTransaction())
                {
                    try
                    {
                        // Insert into accounts table
                        using (var cmd = new MySqlCommand(
                            "INSERT INTO accounts (accountname, password, qq) VALUES (@user, @pass, @qq)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@user", username);
                            cmd.Parameters.AddWithValue("@pass", hashedPassword);
                            cmd.Parameters.AddWithValue("@qq", password);
                            cmd.ExecuteNonQuery();
                        }

                        // Get UID
                        using (var cmd = new MySqlCommand(
                            "SELECT uid FROM accounts WHERE accountname = @user", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@user", username);
                            var result = cmd.ExecuteScalar();
                            if (result == null)
                                throw new Exception("Failed to get account UID");
                            uid = Convert.ToInt32(result);
                        }

                        using (var cmd = new MySqlCommand(
                            "INSERT IGNORE INTO limit_create_character (m_id) VALUES (@uid)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@uid", uid);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new MySqlCommand(
                            "INSERT IGNORE INTO member_info (m_id, user_id) VALUES (@uid, @userid)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@uid", uid);
                            cmd.Parameters.AddWithValue("@userid", uid.ToString());
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new MySqlCommand(
                            "INSERT IGNORE INTO member_white_account (m_id) VALUES (@uid)", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@uid", uid);
                            cmd.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }

            // Insert into taiwan_login database
            var loginConnStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_login;Uid={DB_USER};Pwd={DB_PASS};";
            using (var loginConn = new MySqlConnection(loginConnStr))
            {
                loginConn.Open();
                using (var cmd = new MySqlCommand(
                    "INSERT IGNORE INTO member_login (m_id) VALUES (@uid)", loginConn))
                {
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.ExecuteNonQuery();
                }
            }
        }
        #endregion


        #region Dashboard
        private void UpdateDashboard()
        {
            if (_currentSession == null) return;

            CeraDisplay.Text = $"{_currentSession.Cera:N0}";
            CharacterList.SelectionChanged -= CharacterList_SelectionChanged;
            CharacterList.ItemsSource = null;
            CharacterList.ItemsSource = _currentSession.Characters;
            CharacterList.SelectionChanged += CharacterList_SelectionChanged;

            if (_currentSession.Characters.Count > 0)
            {
                CharacterList.SelectedIndex = 0;
            }
        }

        private void CharacterList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                StatHP.Text = "0";
                StatMP.Text = "0";
                StatPhyAtk.Text = "0";
                StatMagAtk.Text = "0";
                StatFatigue.Text = "0/156";
                StatLastPlayed.Text = "Never";
                return;
            }

            StatHP.Text = $"{selected.MaxHP:N0}";
            StatMP.Text = $"{selected.MaxMP:N0}";
            StatPhyAtk.Text = $"{selected.PhyAttack:N0}";
            StatMagAtk.Text = $"{selected.MagAttack:N0}";
            StatFatigue.Text = $"{selected.Fatigue}/{selected.MaxFatigue}";
            StatLastPlayed.Text = selected.LastPlayTime == DateTime.MinValue ? "Never" : selected.LastPlayTime.ToString("yyyy-MM-dd HH:mm");
            LevelBox.Text = selected.Level.ToString();
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshData();
        }

        private async void RefreshData()
        {
            if (_currentSession == null) return;

            var username = UsernameBox.Text;
            var password = PasswordBox.Password;

            SetStatus("Refreshing...", (Brush)FindResource("TextSecondaryBrush"));

            try
            {
                var session = await Task.Run(() => PerformLogin(username, password));
                _currentSession = session;
                UpdateDashboard();
                SetStatus("Data refreshed", (Brush)FindResource("SuccessBrush"));
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush"));
            }
        }

        private async void SendGoldBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSession == null) return;

            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            if (!int.TryParse(AmountBox.Text, out int amount) || amount <= 0)
            {
                SetStatus("Enter a valid amount", (Brush)FindResource("WarningBrush"));
                return;
            }

            var charId = selected.Id;
            var charName = selected.Name;

            try
            {
                await Task.Run(() => SendGold(charId, amount));
                Dispatcher.Invoke(() => SetStatus($"Sent {amount:N0} Gold to {charName}", (Brush)FindResource("GoldBrush")));

                await Task.Delay(500);
                Dispatcher.Invoke(() => RefreshData());
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private async void SendCeraBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSession == null) return;

            if (!int.TryParse(AmountBox.Text, out int amount) || amount <= 0)
            {
                SetStatus("Enter a valid amount", (Brush)FindResource("WarningBrush"));
                return;
            }

            var uid = _currentSession.Uid;

            try
            {
                await Task.Run(() => SendCera(uid, amount));
                Dispatcher.Invoke(() => SetStatus($"Sent {amount:N0} CERA", (Brush)FindResource("TextAccentBrush")));

                await Task.Delay(500);
                Dispatcher.Invoke(() => RefreshData());
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void SendGold(int charId, int amount)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain_2nd;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("UPDATE inventory SET money = money + @amt WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@amt", amount);
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void SendCera(int uid, int amount)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_billing;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                var sql = @"INSERT INTO cash_cera (account, cera, mod_tran, mod_date, reg_date)
                            VALUES (@uid, @amt, 1, NOW(), NOW())
                            ON DUPLICATE KEY UPDATE cera = cera + @amt2";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Parameters.AddWithValue("@amt", amount);
                    cmd.Parameters.AddWithValue("@amt2", amount);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSession == null) return;

            try
            {
                Process.Start(_gamePath, _currentSession.Token);
                SetStatus("Launching game...", (Brush)FindResource("SuccessGlowBrush"));
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush"));
            }
        }

        private void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            _currentSession = null;
            SwitchToLogin();
        }

        private async void ResetFatigueBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => ResetFatigue(selected.Id));
                Dispatcher.Invoke(() =>
                {
                    SetStatus($"Fatigue reset for {selected.Name}", (Brush)FindResource("SuccessBrush"));
                    RefreshData();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void ResetFatigue(int charId)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("UPDATE charac_info SET fatigue = 0 WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private async void SetVIPBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => SetVIP(selected.Id));
                Dispatcher.Invoke(() =>
                {
                    SetStatus($"VIP set for {selected.Name}", (Brush)FindResource("SuccessBrush"));
                    RefreshData();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void SetVIP(int charId)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("UPDATE charac_info SET VIP = 'VIP' WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private async void SetLevelBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            if (!int.TryParse(LevelBox.Text, out int level) || level < 1 || level > 100)
            {
                SetStatus("Enter a valid level (1-100)", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => SetLevel(selected.Id, level));
                Dispatcher.Invoke(() =>
                {
                    SetStatus($"Level set to {level} for {selected.Name}", (Brush)FindResource("SuccessBrush"));
                    RefreshData();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void SetLevel(int charId, int level)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("UPDATE charac_info SET lev = @lev WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@lev", level);
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void SetMaxLevel(int charId)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("UPDATE charac_info SET lev = 95, exp = 0 WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private async void SendItemBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            if (!int.TryParse(ItemIdBox.Text, out int itemId) || itemId <= 0)
            {
                SetStatus("Enter a valid item ID", (Brush)FindResource("WarningBrush"));
                return;
            }

            if (!int.TryParse(ItemQtyBox.Text, out int qty) || qty <= 0)
            {
                qty = 1;
            }

            try
            {
                await Task.Run(() => SendItem(selected.Id, selected.Name, itemId, qty));
                Dispatcher.Invoke(() =>
                {
                    SetStatus($"Sent item {itemId} x{qty} to {selected.Name}", (Brush)FindResource("SuccessBrush"));
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void SendItem(int charId, string charName, int itemId, int qty)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain_2nd;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                var sql = @"INSERT INTO postal (occ_time, send_charac_no, send_charac_name, receive_charac_no,
                            item_id, add_info, endurance, upgrade, amplify_option, amplify_value, gold,
                            receive_time, delete_flag, avata_flag, unlimit_flag, seal_flag, creature_flag,
                            postal, letter_id, extend_info, ipg_db_id, ipg_transaction_id, ipg_nexon_id,
                            auction_id, random_option, seperate_upgrade, type, item_guid)
                            VALUES (NOW(), 0, 'GM', @charId, @itemId, @qty, 0, 0, 0, 0, 0,
                            '0000-00-00 00:00:00', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '', 0, '', 0, 0, '')";
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@charId", charId);
                    cmd.Parameters.AddWithValue("@itemId", itemId);
                    cmd.Parameters.AddWithValue("@qty", qty);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private async void MaxLevelBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => SetMaxLevel(selected.Id));
                Dispatcher.Invoke(() =>
                {
                    SetStatus($"Max level set for {selected.Name}", (Brush)FindResource("SuccessBrush"));
                    RefreshData();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private async void SkillResetBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => ResetSkills(selected.Id));
                Dispatcher.Invoke(() => SetStatus($"Skills reset for {selected.Name}", (Brush)FindResource("SuccessBrush")));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void ResetSkills(int charId)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain_2nd;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("DELETE FROM skill WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private async void PvPResetBtn_Click(object sender, RoutedEventArgs e)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => ResetPvP(selected.Id));
                Dispatcher.Invoke(() => SetStatus($"PvP stats reset for {selected.Name}", (Brush)FindResource("SuccessBrush")));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        private void ResetPvP(int charId)
        {
            var connStr = $"Server={DB_HOST};Port={DB_PORT};ConvertZeroDateTime=True;Database=taiwan_cain_2nd;Uid={DB_USER};Pwd={DB_PASS};";
            using (var conn = new MySqlConnection(connStr))
            {
                conn.Open();
                using (var cmd = new MySqlCommand("DELETE FROM pvp_result WHERE charac_no = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", charId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private async void Gold1MBtn_Click(object sender, RoutedEventArgs e)
        {
            await SendGoldPackage(1000000);
        }

        private async void Gold10MBtn_Click(object sender, RoutedEventArgs e)
        {
            await SendGoldPackage(10000000);
        }

        private async void Gold100MBtn_Click(object sender, RoutedEventArgs e)
        {
            await SendGoldPackage(100000000);
        }

        private async Task SendGoldPackage(int amount)
        {
            var selected = CharacterList.SelectedItem as Character;
            if (selected == null)
            {
                SetStatus("Select a character first", (Brush)FindResource("WarningBrush"));
                return;
            }

            try
            {
                await Task.Run(() => SendGold(selected.Id, amount));
                Dispatcher.Invoke(() =>
                {
                    SetStatus($"Sent {amount:N0} Gold to {selected.Name}", (Brush)FindResource("GoldBrush"));
                    RefreshData();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => SetStatus("Error: " + ex.Message, (Brush)FindResource("ErrorBrush")));
            }
        }

        #endregion

        #region Helpers
        private string ComputeMd5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }

        private string GetJobName(int jobId)
        {
            switch (jobId)
            {
                case 0: return "Male Slayer";
                case 1: return "Female Fighter";
                case 2: return "Male Gunner";
                case 3: return "Female Mage";
                case 4: return "Male Priest";
                case 5: return "Female Gunner";
                case 6: return "Thief";
                case 7: return "Male Fighter";
                case 8: return "Male Mage";
                case 9: return "Female Priest";
                case 10: return "Female Slayer";
                default: return "Unknown";
            }
        }

        private string GenerateLoginToken(int uid)
        {
            var privateKeyPem = @"-----BEGIN PRIVATE KEY-----
MIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQC9MLEI8wXuIf4C
pNGQUmhxXeZ5ZFO9VflVl5OqBDCao/05pjxTPPHMx4r17dTXHyUP24luZKqh/r2K
mBrUYB9B7tL/Trq9aPrnpz6MjtAKktTHYmSV4t0DENIKglCUT5SqXv4EpznYm574
IX/hi9z+B1cI4KTZmvXCVt0KlBGaKx097FKrG5GKXvroXKK7UhwwmHuXAqAh+d1i
UUUPZImGR6WuF276BpK0qC3yrr1Ojo/BK/oD2Uxm9T3W0q62O9YRI5iSPrNHtKPl
uUxiUI3xomiPtDYBZ+bjv9LPnRV3uEZ78PuMAV6fCk1Ue3afjUImCCn/2CbL9hlx
PIuE7PgtAgMBAAECggEAHXS0D6Bk4zl0Jr26KiYGgGYeEPbrqc41upuVwFIULjOL
pNrqyAv0Ws2Tp2wu6Ap/lvs3p9IxFfVHVgmOHdRUcYvJWrpLjVuHuyMZNEG1Bvxq
+Bsr7YFLp2NKTJwTBzBnxWnyU0+lDEJYiyoOEtQXpY6HgMiXKhE8I9Sp6DB7GB1L
TZyzpT9vSDuLnH4kaCCA4m6/qBM8R6Bmit/Z05Mvyq6gJsdBtXbMZgJyswn1koF6
BhTNuTXyrcO9lrSRiPFIku4F30rYOYULJafACJ6cs5RkN0fIUESYCnnwnhgXpw6N
FHJ2HMSEwey04vGhdqKFpChx5ocwiHBhrno+bOVbGQKBgQDg+omHeXADRtP85dw+
BqkVgdqs7HCWYQUU2O2uq/LT2rYTHq4EXyRrFuaYCGJPGOZi0MOiAFGJGqOIihj0
7gCQ8bfdYP9QXolu0s02h3PBru1kuVJVPjdxPm3W2AFBkiThiNA0BCBpFvcTOwwT
+kuayyzEGQQ5EZYIAZ/YOcfNqQKBgQDXRt3qucTIIh3NqAmWez8rehhx1fh15wyM
/nf5LXJKP9uwZKmptdwn3AFhl8PRsaNrpA91gviPBsmcgDKQ9MMwMX0VyVSfpeFP
b/EKvNI9cAvosXul6lYhAcRJWlJXuOMxMQieDZ+6QR5aMME1ZCUjvzw23fm6y2TW
lmHp1uIA5QKBgCVeuFV+gHKq4y+Q5uKOrKtb5Hzw0UrJVtS9q81l1nIGVFQctn8X
Zq87IJaEXgARfMNRNg3Ey8ZgXGWjur2EgyeQXyAwqngpG98CuP+jxECZ0+j1N43d
RcxTuF8Fhj/kDKhB14OsY83Q+L2DA2CWJNTTht4T4bWxDCDMVEbQYjXxAoGAdkoX
aIH5MeslKzsZQZmpRU+KnQpwwwBZMiQlckLZmRjrs0osu/cU6MYH8EM/MzsDzALT
B8QWpiiZoagDoQkNM68Nx2ngWPUCD+83qKnGcEHgGVVk1u8jsnFRFOlPc6pBFGeY
D5j22pYrgm1lzNuhWLoc8R0Zut1GJG9vj9kmSE0CgYEAmxBzc5mIXbAvtBj8N14x
2cxc1r7Mx0tjkpU0LwmjFYEwx1qk4VuchN7OA8GqXi5n/lF9C505/QC+RpW7wU9l
Cx4uIecQ9jQLOQIOYL8XvSDCQAj8ZllGw5pu1gyzLMH/FCn//fz96+fF9xw5HNwD
8soJoGND6nCChWgQ36yED/Q=
-----END PRIVATE KEY-----";

            try
            {
                // Parse the private key using BouncyCastle
                RsaPrivateCrtKeyParameters privateKey;
                using (var reader = new StringReader(privateKeyPem))
                {
                    var pemReader = new PemReader(reader);
                    var keyObject = pemReader.ReadObject();
                    privateKey = (RsaPrivateCrtKeyParameters)keyObject;
                }

                var preStr = "1FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF00";
                var nextStr = "010101010101010101010101010101010101010101010101010101010101010155914510010403030101";
                var uidHex = uid.ToString("X8");
                var srcStr = preStr + uidHex + nextStr;

                // Parse hex string to BigInteger
                var message = new BigInteger(srcStr, 16);

                // RSA raw operation: encrypted = message^d mod n
                var encrypted = message.ModPow(privateKey.Exponent, privateKey.Modulus);

                // Convert to hex and then to bytes
                var encryptedHex = encrypted.ToString(16);
                if (encryptedHex.Length % 2 != 0)
                    encryptedHex = "0" + encryptedHex;

                var encryptedBytes = HexToBytes(encryptedHex);
                return Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private byte[] HexToBytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }
        #endregion
    }

    #region Models
    public class DbConfig
    {
        public string DatabaseHost { get; set; } = "192.168.200.131";
        public string DatabaseUser { get; set; } = "game";
        public string DatabasePassword { get; set; } = "uu5!^%jg";
        public int DatabasePort { get; set; } = 3306;
    }

    public class Config
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public bool Remember { get; set; } = false;
        public string GamePath { get; set; } = "DNF.exe";
        public string Server { get; set; } = "192.168.200.131";
    }

    public class Character
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Level { get; set; }
        public string Job { get; set; }
        public long Money { get; set; }
        public int MaxHP { get; set; }
        public int MaxMP { get; set; }
        public int PhyAttack { get; set; }
        public int MagAttack { get; set; }
        public int Fatigue { get; set; }
        public int MaxFatigue { get; set; }
        public DateTime LastPlayTime { get; set; }

        public string LevelDisplay => $"Lv.{Level}";
        public string GoldDisplay => $"{Money:N0} G";
    }

    public class LoginSession
    {
        public int Uid { get; set; }
        public string Token { get; set; }
        public List<Character> Characters { get; set; }
        public long Cera { get; set; }
    }
    #endregion
}
