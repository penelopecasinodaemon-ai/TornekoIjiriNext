using System.Globalization;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace TornekoIjiriNext;

internal enum ConnectionState
{
    Disconnected,
    Connecting,
    FetchingGameInfo,
    WaitingForGame,
    Connected,
    UnsupportedGame,
    ConnectionError
}

internal sealed record CapturedRegion(uint Address, int Length, string BytesHex);
internal sealed record PointerBatchCapture(
    int FormatVersion,
    DateTime CapturedAtUtc,
    string Pcsx2Version,
    string GameTitle,
    string GameId,
    string GameCrc,
    string GameVersion,
    uint StatusBefore,
    uint StatusAfter,
    IReadOnlyList<CapturedRegion> Regions);

internal sealed class MainForm : Form
{
    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 130 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 28011, Width = 95 };
    private readonly Button _connect = new() { Text = "PCSX2に接続", AutoSize = true };
    private readonly Label _state = new() { Text = "未接続", AutoSize = true, Font = new Font("Yu Gothic UI", 10, FontStyle.Bold), ForeColor = Color.Gray };
    private readonly Label _game = new() { Text = "ゲーム情報: 未取得", AutoSize = true, MaximumSize = new Size(800, 0) };
    private readonly TextBox _address = new() { Text = "00139D07", Width = 120 };
    private readonly NumericUpDown _length = new() { Minimum = 1, Maximum = 4096, Value = 16, Width = 75 };
    private readonly TextBox _memory = new() { Multiline = true, ScrollBars = ScrollBars.Both, AcceptsReturn = true, AcceptsTab = true, WordWrap = false, Font = new Font("Consolas", 10), Dock = DockStyle.Fill };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button _read = new() { Text = "読み込み", AutoSize = true, Enabled = false };
    private readonly Button _write = new() { Text = "現在値を書き込み…", AutoSize = true, Enabled = false };
    private readonly Button _pointerDump = new() { Text = "実測参照先16領域を読み取り", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _monitor = new() { Interval = 1000 };
    private PineClient? _pine;
    private GameInfo? _gameInfo;
    private string? _pcsx2Version;
    private ConnectionState _connectionState = ConnectionState.Disconnected;
    private bool _gameActive;
    private int _polling;
    private int _connectionGeneration;

    public MainForm()
    {
        Text = "トルネコいじり Next";
        MinimumSize = new Size(900, 700);
        Size = new Size(1120, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.5f);
        BuildUi();
        _connect.Click += async (_, _) => await ConnectPineAsync();
        _read.Click += async (_, _) => await ReadMemoryAsync();
        _write.Click += async (_, _) => await WriteMemoryAsync();
        _pointerDump.Click += async (_, _) => await ReadPointerTargetsAsync();
        _monitor.Tick += async (_, _) => await MonitorConnectionAsync();
        FormClosed += (_, _) => Disconnect();
        SetState(ConnectionState.Disconnected, "PCSX2に未接続です。");
        Log("PINEを使ってPCSX2へ接続します。標準ポートは28011です。");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        Controls.Add(root);

        root.Controls.Add(new Label { Text = "トルネコいじり Next", Font = new Font("Yu Gothic UI", 20, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(27, 52, 78), Padding = new Padding(0, 0, 0, 10) }, 0, 0);
        var conn = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(10), BackColor = Color.FromArgb(239, 244, 248), Margin = new Padding(0, 0, 0, 12) };
        conn.Controls.Add(new Label { Text = "接続先", AutoSize = true, Margin = new Padding(3, 8, 2, 0) });
        conn.Controls.Add(_host);
        conn.Controls.Add(new Label { Text = "ポート", AutoSize = true, Margin = new Padding(10, 8, 2, 0) });
        conn.Controls.Add(_port);
        conn.Controls.Add(_connect);
        conn.Controls.Add(new Label { Text = "状態:", AutoSize = true, Margin = new Padding(12, 8, 2, 0) });
        conn.Controls.Add(_state);
        conn.Controls.Add(_game);
        root.Controls.Add(conn, 0, 1);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 2, 0, 8) };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 8) };
        bar.Controls.Add(new Label { Text = "PS2アドレス", AutoSize = true, Margin = new Padding(3, 8, 2, 0) });
        bar.Controls.Add(_address);
        bar.Controls.Add(new Label { Text = "長さ", AutoSize = true, Margin = new Padding(8, 8, 2, 0) });
        bar.Controls.Add(_length);
        bar.Controls.Add(_read);
        bar.Controls.Add(_write);
        bar.Controls.Add(_pointerDump);
        var validation = new Button { Text = "旧アドレス検証", AutoSize = true };
        validation.Click += async (_, _) => await ValidateLegacyAddressesAsync();
        bar.Controls.Add(validation);
        var catalog = new Button { Text = "旧版マスター一覧", AutoSize = true };
        catalog.Click += (_, _) => new CatalogBrowserForm().ShowDialog(this);
        bar.Controls.Add(catalog);
        var spawnEditor = new Button { Text = "出現テーブル編集（オフライン）", AutoSize = true };
        spawnEditor.Click += (_, _) => new MonsterSpawnTableForm().ShowDialog(this);
        bar.Controls.Add(spawnEditor);
        var spawnCompare = new Button { Text = "出現IDの重複比較（オフライン）", AutoSize = true };
        spawnCompare.Click += (_, _) => new MonsterSpawnCompareForm().ShowDialog(this);
        bar.Controls.Add(spawnCompare);
        var monsterStatus = new Button { Text = "モンスター基本能力（オフライン）", AutoSize = true };
        monsterStatus.Click += (_, _) => new MonsterStatusEditorForm().ShowDialog(this);
        bar.Controls.Add(monsterStatus);
        var monsterGrowth = new Button { Text = "モンスター成長テーブル（オフライン）", AutoSize = true };
        monsterGrowth.Click += (_, _) => new MonsterGrowthTableForm().ShowDialog(this);
        bar.Controls.Add(monsterGrowth);
        var floorEditor = new Button { Text = "フロア設定編集（オフライン）", AutoSize = true };
        floorEditor.Click += (_, _) => new FloorDataEditorForm().ShowDialog(this);
        bar.Controls.Add(floorEditor);
        var itemRateEditor = new Button { Text = "アイテムカテゴリ率（オフライン）", AutoSize = true };
        itemRateEditor.Click += (_, _) => new ItemCategoryRateForm().ShowDialog(this);
        bar.Controls.Add(itemRateEditor);
        var individualItemRates = new Button { Text = "個別アイテム率（オフライン）", AutoSize = true };
        individualItemRates.Click += (_, _) => new ItemIndividualRateForm().ShowDialog(this);
        bar.Controls.Add(individualItemRates);
        var snapshotInspector = new Button { Text = "旧snapshot閲覧・比較（オフライン）", AutoSize = true };
        snapshotInspector.Click += (_, _) => new LegacyMemorySnapshotForm().ShowDialog(this);
        bar.Controls.Add(snapshotInspector);
        var legacyIni = new Button { Text = "旧INI設定（オフライン）", AutoSize = true };
        legacyIni.Click += (_, _) => new LegacyIniForm().ShowDialog(this);
        bar.Controls.Add(legacyIni);
        var projectBundle = new Button { Text = "旧データプロジェクト", AutoSize = true };
        projectBundle.Click += (_, _) => new ProjectBundleForm().ShowDialog(this);
        bar.Controls.Add(projectBundle);
        var roomEditor = new Button { Text = "固定部屋マップ（オフライン）", AutoSize = true };
        roomEditor.Click += (_, _) => new FixedRoomMapForm().ShowDialog(this);
        bar.Controls.Add(roomEditor);
        var capture = new Button { Text = "調査キャプチャ・比較", AutoSize = true };
        capture.Click += (_, _) => new MemoryCaptureForm(() => _pine, IsCaptureSessionActive).ShowDialog(this);
        bar.Controls.Add(capture);
        var binaryEditor = new Button { Text = "汎用バイナリエディタ（オフライン）", AutoSize = true };
        binaryEditor.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "編集するバイナリファイルを選択",
                Filter = "バイナリ・データファイル (*.bin;*.dat;*.raw;*.sav;*.dmp)|*.bin;*.dat;*.raw;*.sav;*.dmp|すべてのファイル (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { new LegacyBinaryEditorForm(dialog.FileName).ShowDialog(this); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ファイルを開けません", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        bar.Controls.Add(binaryEditor);
        main.Controls.Add(bar, 0, 0);
        main.Controls.Add(_memory, 0, 1);
        main.Controls.Add(new Label { Text = "読み書き形式: 16進数バイト列。書き込みは対象ID確認・変更前値確認・最終確認・読み戻しを行います。", AutoSize = true, ForeColor = Color.DimGray }, 0, 2);
        root.Controls.Add(main, 0, 2);

        var logGroup = new GroupBox { Text = "接続・操作ログ", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 3);
    }

    private async Task ConnectPineAsync()
    {
        _monitor.Stop();
        ++_connectionGeneration;
        _pine?.Dispose();
        _pine = null;
        _gameInfo = null;
        _pcsx2Version = null;
        _gameActive = false;
        SetState(ConnectionState.Connecting, "PINE TCPに接続中…");

        try
        {
            var client = await Task.Run(() => PineClient.Connect(_host.Text.Trim(), (int)_port.Value));
            _pine = client;
            _pcsx2Version = await Task.Run(client.GetPcsx2Version);
            _gameActive = await Task.Run(() => client.GetStatus() is 0 or 1);
            if (_gameActive)
                await RefreshGameInfoAsync(client, _connectionGeneration);
            else
                SetState(ConnectionState.WaitingForGame, "PCSX2には接続しました。ゲーム起動を待っています。");

            _monitor.Start();
            Log($"PINE接続成功: PCSX2={_pcsx2Version}, ゲーム={_gameInfo?.Title ?? "ゲーム待機中"}, ID={_gameInfo?.Id ?? "未取得"}");
        }
        catch (Exception ex)
        {
            if (_pine is null)
                SetState(ConnectionState.ConnectionError, "PINEへの接続に失敗しました。PCSX2とポート設定を確認してください。");
            else
                HandleConnectionFailure(ex, _pine);
            Log($"接続/ゲーム情報取得失敗: {ex.Message}");
        }
    }

    private async Task RefreshGameInfoAsync(PineClient client, int generation)
    {
        SetState(ConnectionState.FetchingGameInfo, "PINEからゲーム情報を取得中…");
        var info = await Task.Run(client.GetGameInfo);
        if (generation != _connectionGeneration || !ReferenceEquals(client, _pine)) return;
        _gameInfo = info;
        _gameActive = true;
        if (string.Equals(info.Id, PineClient.AllowedGameId, StringComparison.Ordinal))
            SetState(ConnectionState.Connected, "対象ゲームを確認しました。書き込みを許可できます。");
        else
            SetState(ConnectionState.UnsupportedGame, $"対象外ゲームです。許可ID: {PineClient.AllowedGameId}");
    }

    private async Task MonitorConnectionAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0) return;
        var client = _pine;
        var generation = _connectionGeneration;
        if (client is null) { Interlocked.Exchange(ref _polling, 0); return; }
        try
        {
            var status = await Task.Run(client.GetStatus);
            if (generation != _connectionGeneration || !ReferenceEquals(client, _pine)) return;
            if (status == 2)
            {
                _gameInfo = null;
                _gameActive = false;
                SetState(ConnectionState.WaitingForGame, "PCSX2接続中・ゲーム未起動（起動を待っています）。");
                return;
            }
            await RefreshGameInfoAsync(client, generation);
        }
        catch (Exception ex)
        {
            if (generation != _connectionGeneration) return;
            HandleConnectionFailure(ex, client);
            Log($"接続監視: {ex.Message}");
        }
        finally { Interlocked.Exchange(ref _polling, 0); }
    }

    private void HandleConnectionFailure(Exception ex, PineClient? client)
    {
        if (ex is PineCommandException)
        {
            _gameInfo = null;
            _gameActive = false;
            SetState(ConnectionState.ConnectionError, "PINEコマンド失敗。ゲーム状態を再確認します。");
            return; // PINE command errors do not necessarily close the TCP session.
        }

        if (client is not null && ReferenceEquals(client, _pine))
        {
            ++_connectionGeneration;
            _monitor.Stop();
            client.Dispose();
            _pine = null;
        }
        _gameInfo = null;
        _gameActive = false;
        if (ex is EndOfStreamException or SocketException or ObjectDisposedException)
            SetState(ConnectionState.Disconnected, "PCSX2/PINEとのTCP接続が切断されました。書き込みは禁止されています。");
        else
            SetState(ConnectionState.ConnectionError, "PINE通信エラー。再接続してください。");
    }

    private void Disconnect()
    {
        _monitor.Stop();
        ++_connectionGeneration;
        _pine?.Dispose();
        _pine = null;
        _gameInfo = null;
        _pcsx2Version = null;
        _gameActive = false;
        SetState(ConnectionState.Disconnected, "PCSX2に未接続です。");
    }

    private bool CanWrite => _pine is not null && _gameActive && _connectionState == ConnectionState.Connected &&
        _gameInfo is not null && string.Equals(_gameInfo.Id, PineClient.AllowedGameId, StringComparison.Ordinal);

    private bool IsCaptureSessionActive(PineClient client) => ReferenceEquals(client, _pine) && _gameActive &&
        _connectionState is ConnectionState.Connected or ConnectionState.UnsupportedGame;

    private void SetState(ConnectionState state, string detail)
    {
        _connectionState = state;
        _state.Text = state switch
        {
            ConnectionState.Disconnected => "未接続",
            ConnectionState.Connecting => "接続中",
            ConnectionState.FetchingGameInfo or ConnectionState.WaitingForGame => "ゲーム情報取得中",
            ConnectionState.Connected => "接続中",
            ConnectionState.UnsupportedGame => "対象外ゲーム",
            _ => "接続エラー"
        };
        _state.ForeColor = state switch
        {
            ConnectionState.Connected => Color.SeaGreen,
            ConnectionState.UnsupportedGame => Color.DarkOrange,
            ConnectionState.ConnectionError => Color.Firebrick,
            ConnectionState.FetchingGameInfo => Color.SteelBlue,
            ConnectionState.WaitingForGame => Color.SteelBlue,
            ConnectionState.Connecting => Color.SteelBlue,
            _ => Color.Gray
        };
        _game.Text = _gameInfo is null
            ? $"PCSX2 {_pcsx2Version ?? ""} / ゲーム情報: {detail}"
            : $"PCSX2 {_pcsx2Version} / {_gameInfo.Title} / {_gameInfo.Id} / CRC {_gameInfo.Crc} / {_gameInfo.GameVersion} — {detail}";
        _connect.Enabled = state is not (ConnectionState.Connecting or ConnectionState.FetchingGameInfo);
        _read.Enabled = _pine is not null && _gameActive && state is ConnectionState.Connected or ConnectionState.UnsupportedGame;
        _write.Enabled = CanWrite;
        _pointerDump.Enabled = _pine is not null && _gameActive && state is ConnectionState.Connected or ConnectionState.UnsupportedGame;
    }

    private bool TryGetAddress(out uint address)
    {
        var text = _address.Text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address)) return true;
        MessageBox.Show(this, "アドレスを16進数で入力してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private async Task ReadMemoryAsync()
    {
        var client = _pine;
        if (client is null || !_gameActive || !TryGetAddress(out var address)) return;
        try
        {
            var data = await Task.Run(() => client.ReadMemory(address, (int)_length.Value));
            if (!ReferenceEquals(client, _pine)) return;
            _memory.Text = string.Join(" ", data.Select(b => b.ToString("X2")));
            Log($"読み込み: 0x{address:X8} から {data.Length} bytes");
        }
        catch (Exception ex) { HandleConnectionFailure(ex, client); Log($"読み込み失敗: {ex.Message}"); }
    }

    private async Task WriteMemoryAsync()
    {
        // UI-level gate; PineClient.SafeWriteMemory repeats all security checks internally.
        var client = _pine;
        var initialInfo = _gameInfo;
        if (!CanWrite || client is null || initialInfo is null)
        {
            Log("書き込み拒否: PINE接続・ゲーム情報・対象ID・接続状態の条件を満たしていません。");
            return;
        }
        if (!TryGetAddress(out var address)) return;
        var data = ParseMemoryBytes();
        if (data is null) return;

        try
        {
            // Revalidate target/session before presenting a write confirmation.
            var status = await Task.Run(client.GetStatus);
            if (status is not (0 or 1)) throw new PineWriteException("ゲームが動作していません。", null, null);
            var freshInfo = await Task.Run(client.GetGameInfo);
            if (!string.Equals(freshInfo.Id, PineClient.AllowedGameId, StringComparison.Ordinal))
            {
                _gameInfo = freshInfo;
                _gameActive = true;
                SetState(ConnectionState.UnsupportedGame, $"対象ID不一致: {freshInfo.Id}");
                throw new PineWriteException($"対象ゲームIDと一致しません: {freshInfo.Id}", null, null);
            }

            var before = await Task.Run(() => client.ReadMemory(address, data.Length));
            var prompt = $"書き込み内容を確認してください。\n\n対象ゲーム: {freshInfo.Title}\nゲームID: {freshInfo.Id}\n接続状態: {status switch { 0 => "動作中", _ => "一時停止" }}\nアドレス: 0x{address:X8}\n書き込み前: {Convert.ToHexString(before)}\n書き込む値: {Convert.ToHexString(data)}\n\n書き込み後に読み戻して一致を確認します。続行しますか？";
            var answer = MessageBox.Show(this, prompt, "メモリ書き込み 最終確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            // Internal guard repeats live status/ID checks and aborts if the old value changed.
            var readBack = await Task.Run(() => client.SafeWriteMemory(address, before, data));
            if (!ReferenceEquals(client, _pine)) throw new IOException("確認中にPINE接続が切り替わりました。");
            _memory.Text = string.Join(" ", readBack.Select(b => b.ToString("X2")));
            Log($"書き込み成功: 0x{address:X8}, 前={Convert.ToHexString(before)}, 書込={Convert.ToHexString(data)}, 読戻={Convert.ToHexString(readBack)}");
            MessageBox.Show(this, "書き込み後の読み戻し値が一致しました。", "書き込み成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (PineWriteException ex)
        {
            Log($"書き込み拒否/失敗: {ex.Message}");
            if (ex.Before is not null) Log($"  書き込み前: {Convert.ToHexString(ex.Before)}");
            if (ex.ReadBack is not null) Log($"  読み戻し: {Convert.ToHexString(ex.ReadBack)} (期待値不一致)");
            var observed = ex.ReadBack ?? ex.Before;
            if (observed is not null) _memory.Text = string.Join(" ", observed.Select(b => b.ToString("X2")));
            MessageBox.Show(this, ex.Message, "書き込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            await MonitorConnectionAsync();
        }
        catch (Exception ex)
        {
            HandleConnectionFailure(ex, client);
            Log($"書き込み失敗: {ex.Message}");
            MessageBox.Show(this, $"書き込みは成功扱いになっていません。\n{ex.Message}", "書き込み失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private byte[]? ParseMemoryBytes()
    {
        try
        {
            var tokens = _memory.Text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 || tokens.Any(t => t.Length > 2 || !byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
                throw new FormatException("2桁までの16進数バイトを空白区切りで入力してください。");
            if (tokens.Length > 4096) throw new FormatException("一度に書き込めるのは4096バイトまでです。");
            return tokens.Select(t => byte.Parse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
    }

    private async Task ValidateLegacyAddressesAsync()
    {
        var client = _pine;
        if (client is null || !_gameActive) { Log("検証にはPINE接続と起動中のゲームが必要です。"); return; }
        var checks = new (string Name, uint Address, int Size)[]
        {
            ("モンスター名アンカー候補", 0x00139D07, 14),
            ("静的ポインタテーブル候補", 0x00180000, 16),
            ("ダンジョンマスター候補", 0x00148EBC, 2),
            ("モンスター出現テーブル候補", 0x00199EC6, 6),
            ("動的ワーク候補", 0x005DBCA8, 2),
        };
        Log("--- 旧アドレス候補読み取り（資料上は未検証） ---");
        foreach (var item in checks)
        {
            try
            {
                var bytes = await Task.Run(() => client.ReadMemory(item.Address, item.Size));
                Log($"{item.Name} 0x{item.Address:X8}: {Convert.ToHexString(bytes)}");
                if (item.Address == 0x00139D07)
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    Log("  Shift-JIS候補文字列: " + Encoding.GetEncoding(932).GetString(bytes));
                }
            }
            catch (Exception ex) { HandleConnectionFailure(ex, client); Log($"{item.Name}: 読み取り不可 ({ex.Message})"); break; }
        }
        Log("読み取れた値だけでは正しいアドレスと断定できません。ゲーム版/シーンと既知データを照合してください。");
    }

    private async Task ReadPointerTargetsAsync()
    {
        var client = _pine;
        if (client is null || !_gameActive || _connectionState is not (ConnectionState.Connected or ConnectionState.UnsupportedGame))
        {
            Log("参照先の読み取りには、PINE接続と起動中ゲームが必要です。");
            return;
        }

        // Addresses are the 16 little-endian values in the user-provided real-hardware
        // capture of 0x00180020. They remain observations, not identified structures.
        uint[] addresses =
        [
            0x00181100, 0x001812C4, 0x001815D8, 0x0018195C,
            0x00181C00, 0x00182064, 0x00182538, 0x00182AEC,
            0x00182CB0, 0x001830A4, 0x00183578, 0x00183A4C,
            0x00184070, 0x00184234, 0x00184628, 0x00184B6C
        ];
        _pointerDump.Enabled = false;
        try
        {
            // Read-only: this path calls only official status/info/read queries. Verify session
            // metadata on both sides because the 16 regions are obtained in separate requests.
            var capture = await Task.Run(() => CapturePointerBatch(client, addresses));
            if (!ReferenceEquals(client, _pine)) return;
            var dumps = capture.Regions.Select(region => (region.Address, Bytes: Convert.FromHexString(region.BytesHex))).ToArray();

            var output = new StringBuilder();
            output.AppendLine($"UTC {capture.CapturedAtUtc:O} / PCSX2 {capture.Pcsx2Version} / {capture.GameTitle} / Game ID {capture.GameId} / CRC {capture.GameCrc} / Version {capture.GameVersion}");
            output.AppendLine($"PCSX2 status before/after: {capture.StatusBefore}/{capture.StatusAfter} (0=running, 1=paused)");
            output.AppendLine("各領域を64 bytes読み取り。DWORDは4-byte little-endian表示。データの意味は未判定。");
            AppendCrossRegionSummary(output, dumps);
            output.AppendLine();
            foreach (var dump in dumps)
            {
                output.AppendLine($"===== 0x{dump.Address:X8} (64 bytes) =====");
                output.AppendLine("HEX bytes:");
                for (var offset = 0; offset < dump.Bytes.Length; offset += 16)
                {
                    var line = Convert.ToHexString(dump.Bytes.AsSpan(offset, 16));
                    var spaced = string.Join(" ", Enumerable.Range(0, 16).Select(i => line.Substring(i * 2, 2)));
                    output.AppendLine($"0x{dump.Address + (uint)offset:X8}: {spaced}");
                }
                output.AppendLine("DWORD (little-endian):");
                for (var offset = 0; offset < dump.Bytes.Length; offset += 4)
                {
                    var value = BinaryPrimitives.ReadUInt32LittleEndian(dump.Bytes.AsSpan(offset, 4));
                    output.AppendLine($"0x{dump.Address + (uint)offset:X8}: 0x{value:X8}");
                }
                output.AppendLine();
            }

            Log("実測参照先候補16領域を各64 bytes読み取りました（読み取り専用、一括結果の前後でゲーム識別情報を照合）。");
            using var viewer = new Form
            {
                Text = "実測参照先候補16領域メモリダンプ（読み取り専用）",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(1000, 760),
                MinimumSize = new Size(800, 600),
                ShowInTaskbar = false
            };
            var viewerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
            viewerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            viewerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var text = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10),
                Text = output.ToString()
            };
            var save = new Button { Text = "一括キャプチャをJSON保存…", AutoSize = true, Anchor = AnchorStyles.Right };
            save.Click += async (_, _) =>
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "PINE参照先一括キャプチャ (*.json)|*.json",
                    DefaultExt = "json",
                    AddExtension = true,
                    FileName = $"pointer_batch_{capture.GameId}_{capture.CapturedAtUtc:yyyyMMdd_HHmmss}.json"
                };
                if (dialog.ShowDialog(viewer) != DialogResult.OK) return;
                try
                {
                    var json = JsonSerializer.Serialize(capture, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(dialog.FileName, json, new UTF8Encoding(false));
                    Log($"16領域一括キャプチャを保存しました: {dialog.FileName}");
                }
                catch (Exception ex) { MessageBox.Show(viewer, ex.Message, "JSON保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            viewerLayout.Controls.Add(text, 0, 0);
            viewerLayout.Controls.Add(save, 0, 1);
            viewer.Controls.Add(viewerLayout);
            viewer.ShowDialog(this);
        }
        catch (Exception ex)
        {
            HandleConnectionFailure(ex, client);
            Log($"参照先の読み取り失敗: {ex.Message}");
            MessageBox.Show(this, $"読み取りに失敗しました。書き込みは行っていません。\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (ReferenceEquals(client, _pine))
                _pointerDump.Enabled = _gameActive && _connectionState is ConnectionState.Connected or ConnectionState.UnsupportedGame;
        }
    }

    private PointerBatchCapture CapturePointerBatch(PineClient client, uint[] addresses)
    {
        var statusBefore = client.GetStatus();
        if (statusBefore is not (0 or 1)) throw new IOException("読み取り前にゲームが動作していないことを確認しました。");
        var infoBefore = client.GetGameInfo();
        var version = client.GetPcsx2Version();
        var capturedAt = DateTime.UtcNow;
        var regions = addresses.Select(address =>
        {
            var bytes = client.ReadMemory(address, 64);
            if (bytes.Length != 64) throw new IOException($"0x{address:X8} の読み取りサイズが64 bytesではありません。");
            return new CapturedRegion(address, bytes.Length, Convert.ToHexString(bytes));
        }).ToArray();
        var statusAfter = client.GetStatus();
        if (statusAfter is not (0 or 1)) throw new IOException("読み取り中にゲームが停止したため、一括キャプチャを破棄しました。");
        var infoAfter = client.GetGameInfo();
        if (!string.Equals(infoBefore.Id, infoAfter.Id, StringComparison.Ordinal) ||
            !string.Equals(infoBefore.Crc, infoAfter.Crc, StringComparison.Ordinal) ||
            !string.Equals(infoBefore.GameVersion, infoAfter.GameVersion, StringComparison.Ordinal))
            throw new IOException("一括読み取り中にゲームが切り替わったため、キャプチャを破棄しました。");
        return new PointerBatchCapture(1, capturedAt, version, infoAfter.Title, infoAfter.Id, infoAfter.Crc, infoAfter.GameVersion, statusBefore, statusAfter, regions);
    }

    private static void AppendCrossRegionSummary(StringBuilder output, (uint Address, byte[] Bytes)[] dumps)
    {
        if (dumps.Length == 0 || dumps.Any(dump => dump.Bytes.Length != dumps[0].Bytes.Length)) return;
        var length = dumps[0].Bytes.Length;
        var differingOffsets = Enumerable.Range(0, length)
            .Where(offset => dumps.Select(dump => dump.Bytes[offset]).Distinct().Skip(1).Any())
            .ToArray();
        output.AppendLine();
        output.AppendLine($"===== 領域間の機械的比較 ({dumps.Length}領域 × {length} bytes) =====");
        output.AppendLine($"全領域で一致するbyte offset: {length - differingOffsets.Length}/{length}");
        if (differingOffsets.Length == 0)
        {
            output.AppendLine("差分byteなし。");
            return;
        }
        output.AppendLine("値が異なるbyte offset (意味は未判定):");
        foreach (var offset in differingOffsets)
        {
            output.AppendLine($"+0x{offset:X2}: " + string.Join(", ", dumps.Select(dump => $"0x{dump.Address:X8}={dump.Bytes[offset]:X2}")));
        }
    }

    private void Log(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        if (!IsDisposed) _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
