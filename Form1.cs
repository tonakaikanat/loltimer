using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Media;
using System.Collections.Generic; // Listを使うために追加

namespace LoLTimerGUI
{
    public partial class Form1 : Form
    {
        //出力、監視ファイル名
        private const string GameProcessName = "League of Legends";
        private const string LogFileName = "lol_play_log.csv";
        private const string SettingsFileName = "settings.txt";

        // デフォルト設定値
        private int practiceThresholdMinutes = 20;
        private int breakTimeMinutes = 5;

        private bool isGaming = false;
        private DateTime startTime;
        private DateTime breakEndTime;

        // タイマー
        private System.Windows.Forms.Timer processCheckTimer;
        private System.Windows.Forms.Timer breakTimer;

        // UI部品
        private TabControl tabControl;
        
        // メインタブ用
        private Label lblStatus;
        private Label lblTotalTime;
        private Button btnToggleMonitor;

        // まとめタブ用
        private ListView lstDaily;
        private ListView lstDetails;

        // 設定タブ用
        private NumericUpDown numThreshold;
        private NumericUpDown numBreak;

        public Form1()
        {
            this.Text = "LoL Play Tracker";
            this.Size = new Size(420, 550);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle; 
            this.MaximizeBox = false;                          

            LoadSettings(); // 設定の読み込み
            InitializeLogFile();

            //タブコントロールの作成
            tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("Meiryo", 10) };
            
            TabPage tabMain = new TabPage("メイン");
            TabPage tabSummary = new TabPage("集計");
            TabPage tabSettings = new TabPage("設定");

            tabControl.TabPages.Add(tabMain);
            tabControl.TabPages.Add(tabSummary);
            tabControl.TabPages.Add(tabSettings);
            this.Controls.Add(tabControl);


            //メインタブの構築
            lblStatus = new Label { Text = "ステータス: 監視停止中", Location = new Point(20, 40), Size = new Size(360, 30), Font = new Font("Meiryo", 14, FontStyle.Bold), ForeColor = Color.Gray };
            tabMain.Controls.Add(lblStatus);

            lblTotalTime = new Label { Text = "今日の合計: 0 分", Location = new Point(20, 100), Size = new Size(360, 30), Font = new Font("Meiryo", 16) };
            tabMain.Controls.Add(lblTotalTime);

            btnToggleMonitor = new Button { Text = "▶ 監視を開始する", Location = new Point(20, 380), Size = new Size(350, 60), Font = new Font("Meiryo", 14, FontStyle.Bold), BackColor = Color.LightGreen };
            btnToggleMonitor.Click += BtnToggleMonitor_Click;
            tabMain.Controls.Add(btnToggleMonitor);

            //まとめタブの構築
            Label lblDaily = new Label { Text = "▼ 日付ごとの合計（クリックで詳細表示）", Location = new Point(10, 10), Size = new Size(360, 20) };
            tabSummary.Controls.Add(lblDaily);

            lstDaily = new ListView { Location = new Point(10, 35), Size = new Size(370, 180), View = View.Details, FullRowSelect = true, GridLines = true };
            lstDaily.Columns.Add("日付", 150);
            lstDaily.Columns.Add("合計時間(分)", 150);
            lstDaily.SelectedIndexChanged += LstDaily_SelectedIndexChanged; // クリック時のイベント
            tabSummary.Controls.Add(lstDaily);

            Label lblDetails = new Label { Text = "▼ 選択した日の詳細データ", Location = new Point(10, 230), Size = new Size(360, 20) };
            tabSummary.Controls.Add(lblDetails);

            lstDetails = new ListView { Location = new Point(10, 255), Size = new Size(370, 200), View = View.Details, FullRowSelect = true, GridLines = true };
            lstDetails.Columns.Add("開始時刻", 100);
            lstDetails.Columns.Add("時間(分)", 100);
            lstDetails.Columns.Add("種別", 120);
            tabSummary.Controls.Add(lstDetails);

            //タブが切り替わった時にまとめデータを更新する
            tabControl.SelectedIndexChanged += (s, e) => { if (tabControl.SelectedIndex == 1) LoadSummaryData(); };

            //設定タブの構築
            Label lblThDesc = new Label { Text = "練習とみなす閾値（分以下）:", Location = new Point(20, 40), Size = new Size(250, 25) };
            tabSettings.Controls.Add(lblThDesc);
            
            numThreshold = new NumericUpDown { Location = new Point(280, 38), Size = new Size(80, 25), Minimum = 1, Maximum = 120, Value = practiceThresholdMinutes };
            tabSettings.Controls.Add(numThreshold);

            Label lblBrDesc = new Label { Text = "試合後の休憩時間（分）:", Location = new Point(20, 90), Size = new Size(250, 25) };
            tabSettings.Controls.Add(lblBrDesc);

            numBreak = new NumericUpDown { Location = new Point(280, 88), Size = new Size(80, 25), Minimum = 1, Maximum = 60, Value = breakTimeMinutes };
            tabSettings.Controls.Add(numBreak);

            Button btnSave = new Button { Text = "設定を保存する", Location = new Point(20, 150), Size = new Size(340, 40), BackColor = Color.LightSkyBlue };
            btnSave.Click += BtnSaveSettings_Click;
            tabSettings.Controls.Add(btnSave);

            //タイマー初期化
            processCheckTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            processCheckTimer.Tick += ProcessCheckTimer_Tick;

            breakTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            breakTimer.Tick += BreakTimer_Tick;

            UpdateMainTabTotal(); // 今日の合計を初期計算
        }

        //処理ロジック

        private void BtnToggleMonitor_Click(object? sender, EventArgs e)
        {
            if (processCheckTimer.Enabled)
            {
                processCheckTimer.Stop();
                btnToggleMonitor.Text = "▶ 監視を開始する";
                btnToggleMonitor.BackColor = Color.LightGreen;
                lblStatus.Text = "ステータス: 監視停止中";
                lblStatus.ForeColor = Color.Gray;
                isGaming = false;
            }
            else
            {
                processCheckTimer.Start();
                btnToggleMonitor.Text = "■ 監視を停止する";
                btnToggleMonitor.BackColor = Color.LightPink;
                lblStatus.Text = "ステータス: 待機中 (監視中...)";
                lblStatus.ForeColor = Color.Black;
            }
        }

        private void ProcessCheckTimer_Tick(object? sender, EventArgs e)
        {
            bool isProcessRunning = Process.GetProcessesByName(GameProcessName).Length > 0;

            if (isProcessRunning && !isGaming)
            {
                isGaming = true;
                startTime = DateTime.Now;
                lblStatus.Text = $"ステータス: 試合中 ({startTime:HH:mm}～)";
                lblStatus.ForeColor = Color.Red;
            }
            else if (!isProcessRunning && isGaming)
            {
                isGaming = false;
                DateTime endTime = DateTime.Now;
                double duration = Math.Round((endTime - startTime).TotalMinutes, 1);
                string gameType = duration > practiceThresholdMinutes ? "Match" : "Practice";

                RecordMatch(startTime, endTime, duration, gameType);
                UpdateMainTabTotal(); 

                if (gameType == "Match")
                {
                    StartBreak(breakTimeMinutes); 
                }
                else
                {
                    lblStatus.Text = "ステータス: 待機中 (練習完了)";
                    lblStatus.ForeColor = Color.Black;
                }
            }
        }

        private void StartBreak(int minutes)
        {
            breakEndTime = DateTime.Now.AddMinutes(minutes);
            processCheckTimer.Stop(); 
            breakTimer.Start();
            btnToggleMonitor.Enabled = false; 
            SystemSounds.Exclamation.Play(); 
        }

        private void BreakTimer_Tick(object? sender, EventArgs e)
        {
            TimeSpan remaining = breakEndTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                breakTimer.Stop();
                lblStatus.Text = "ステータス: 待機中 (監視中...)";
                lblStatus.ForeColor = Color.Black;
                processCheckTimer.Start(); 
                btnToggleMonitor.Enabled = true; 
                SystemSounds.Asterisk.Play(); 
            }
            else
            {
                lblStatus.Text = $"休憩中... 残り {remaining.Minutes:D2}:{remaining.Seconds:D2}";
                lblStatus.ForeColor = Color.Blue;
            }
        }

        //データと設定の管理

        private void InitializeLogFile()
        {
            if (!File.Exists(LogFileName)) File.WriteAllText(LogFileName, "Date,StartTime,EndTime,DurationMinutes,GameType\n");
        }

        private void RecordMatch(DateTime start, DateTime end, double duration, string gameType)
        {
            File.AppendAllText(LogFileName, $"{start:yyyy-MM-dd},{start:HH:mm:ss},{end:HH:mm:ss},{duration},{gameType}\n");
        }

        private void UpdateMainTabTotal()
        {
            double total = 0;
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            if (File.Exists(LogFileName))
            {
                var lines = File.ReadAllLines(LogFileName).Skip(1);
                foreach (var line in lines)
                {
                    var cols = line.Split(',');
                    if (cols.Length >= 5 && cols[0] == todayStr && double.TryParse(cols[3], out double minutes))
                    {
                        total += minutes;
                    }
                }
            }
            lblTotalTime.Text = $"今日の合計プレイ時間: {Math.Round(total, 1)} 分";
        }

        //まとめタブ：日付ごとのデータを集計して表示
        private void LoadSummaryData()
        {
            lstDaily.Items.Clear();
            lstDetails.Items.Clear(); // 詳細リストもリセット
            if (!File.Exists(LogFileName)) return;

            var lines = File.ReadAllLines(LogFileName).Skip(1);
            var records = lines.Select(l => l.Split(',')).Where(c => c.Length >= 5).ToList();

            // C#のLINQを使って、日付(cols[0])ごとにグループ化し、合計時間を計算
            var grouped = records.GroupBy(c => c[0]);

            foreach (var group in grouped.OrderByDescending(g => g.Key)) // 新しい日付順
            {
                double dailyTotal = group.Sum(c => double.TryParse(c[3], out double m) ? m : 0);
                var item = new ListViewItem(group.Key); // 日付
                item.SubItems.Add(Math.Round(dailyTotal, 1).ToString());
                lstDaily.Items.Add(item);
            }
        }

        //まとめタブ：上のリストをクリックした時、下の詳細リストを更新
        private void LstDaily_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (lstDaily.SelectedItems.Count == 0) return;
            string selectedDate = lstDaily.SelectedItems[0].Text;
            
            lstDetails.Items.Clear();
            var lines = File.ReadAllLines(LogFileName).Skip(1);
            
            foreach (var line in lines)
            {
                var cols = line.Split(',');
                if (cols.Length >= 5 && cols[0] == selectedDate)
                {
                    var item = new ListViewItem(cols[1]); // 開始時刻
                    item.SubItems.Add(cols[3]);           // 時間
                    item.SubItems.Add(cols[4]);           // 種別
                    lstDetails.Items.Add(item);
                }
            }
        }

        //設定の読み込み
        private void LoadSettings()
        {
            if (File.Exists(SettingsFileName))
            {
                var data = File.ReadAllText(SettingsFileName).Split(',');
                if (data.Length == 2)
                {
                    int.TryParse(data[0], out practiceThresholdMinutes);
                    int.TryParse(data[1], out breakTimeMinutes);
                }
            }
        }

        //設定の保存
        private void BtnSaveSettings_Click(object? sender, EventArgs e)
        {
            practiceThresholdMinutes = (int)numThreshold.Value;
            breakTimeMinutes = (int)numBreak.Value;
            File.WriteAllText(SettingsFileName, $"{practiceThresholdMinutes},{breakTimeMinutes}");
            MessageBox.Show("設定を保存しました！", "確認", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}