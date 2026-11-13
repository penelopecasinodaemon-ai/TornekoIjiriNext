using System.Buffers.Binary;
using System.Drawing;
using System.Windows.Forms;

namespace TornekoIjiriNext;

/// <summary>
/// ゲームメモリの数値をマウスで編集する共通コントロール。
///
/// 数値変更時に即時書き込みを行う。
/// アドレスが検証済みでない場合は編集を無効化する。
/// </summary>
internal sealed class PineOnlineNumericEditor : UserControl
{
    private readonly PineOnlineEditService _service;
    private readonly uint _address;
    private readonly int _width;
    private readonly bool _addressVerified;

    private readonly Label _nameLabel;
    private readonly Label _addressLabel;
    private readonly Label _statusLabel;
    private readonly NumericUpDown _valueInput;
    private readonly Button _refreshButton;

    private decimal _lastConfirmedValue;
    private bool _updating;
    private bool _writeInProgress;

    public string FieldName { get; }
    public uint Address => _address;
    public int WidthBytes => _width;
    public bool IsAddressVerified => _addressVerified;

    public decimal CurrentValue => _valueInput.Value;

    public PineOnlineNumericEditor(
        PineOnlineEditService service,
        string fieldName,
        uint address,
        int widthBytes,
        uint initialValue,
        bool addressVerified)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        // 画面側の真偽値だけでは編集を許可しない。
        // サービス側の検証済みアドレス判定を必ず通す。
        addressVerified =
            addressVerified &&
            _service.IsWriteTargetVerified(address, widthBytes);

        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("項目名が必要です。", nameof(fieldName));

        if (widthBytes is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(
                nameof(widthBytes),
                "BYTE・WORD・DWORDのみ対応しています。");

        ulong maxAddress = (ulong)address + (uint)widthBytes - 1;
        if (maxAddress > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(address));

        ulong maxValue = widthBytes switch
        {
            1 => byte.MaxValue,
            2 => ushort.MaxValue,
            4 => uint.MaxValue,
            _ => 0
        };

        if (initialValue > maxValue)
            throw new ArgumentOutOfRangeException(nameof(initialValue));

        FieldName = fieldName;
        _address = address;
        _width = widthBytes;
        _addressVerified = addressVerified;

        Height = 72;
        MinimumSize = new Size(360, 72);
        Width = 520;
        Margin = new Padding(4);
        Padding = new Padding(5);

        _nameLabel = new Label
        {
            Text = fieldName,
            AutoSize = true,
            Location = new Point(5, 5)
        };

        _addressLabel = new Label
        {
            Text = $"アドレス: 0x{address:X8} / {GetWidthName(widthBytes)}",
            AutoSize = true,
            Location = new Point(5, 29)
        };

        _valueInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = maxValue,
            Value = initialValue,
            DecimalPlaces = 0,
            ThousandsSeparator = true,
            Width = 125,
            Location = new Point(250, 5),
            Enabled = addressVerified
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            Location = new Point(5, 52),
            Text = addressVerified
                ? "初期値。ゲームとの同期を確認してください。"
                : "アドレス未検証：オンライン編集無効",
            ForeColor = addressVerified ? SystemColors.ControlText : Color.DarkRed
        };

        _refreshButton = new Button
        {
            Text = "ゲーム値を再取得",
            AutoSize = true,
            Location = new Point(385, 3),
            Enabled = addressVerified
        };

        Controls.Add(_nameLabel);
        Controls.Add(_addressLabel);
        Controls.Add(_valueInput);
        Controls.Add(_statusLabel);
        Controls.Add(_refreshButton);

        _lastConfirmedValue = initialValue;

        _valueInput.ValueChanged += ValueInput_ValueChanged;
        _refreshButton.Click += RefreshButton_Click;

        if (addressVerified)
        {
            _statusLabel.Text = "ゲーム値を読み込んでください。";
        }
    }

    private static string GetWidthName(int width)
    {
        return width switch
        {
            1 => "BYTE",
            2 => "WORD",
            4 => "DWORD",
            _ => "UNKNOWN"
        };
    }

    private void ValueInput_ValueChanged(object? sender, EventArgs e)
    {
        if (_updating || _writeInProgress)
            return;

        if (!_addressVerified)
            return;

        _writeInProgress = true;
        _valueInput.Enabled = false;

        try
        {
            decimal requested = _valueInput.Value;
            bool success;
            string message;

            switch (_width)
            {
                case 1:
                    success = _service.TryWriteByte(
                        _address,
                        decimal.ToByte(requested),
                        out message);
                    break;

                case 2:
                    success = _service.TryWriteWord(
                        _address,
                        decimal.ToUInt16(requested),
                        out message);
                    break;

                case 4:
                    success = _service.TryWriteDword(
                        _address,
                        decimal.ToUInt32(requested),
                        out message);
                    break;

                default:
                    success = false;
                    message = "未対応のデータ幅です。";
                    break;
            }

            if (success)
            {
                _lastConfirmedValue = requested;
                _statusLabel.Text = "反映確認済み";
                _statusLabel.ForeColor = Color.DarkGreen;
            }
            else
            {
                RestoreLastConfirmedValue();

                _statusLabel.Text = "書き込み失敗";
                _statusLabel.ForeColor = Color.DarkRed;

                MessageBox.Show(
                    this,
                    message,
                    "オンライン編集エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            RestoreLastConfirmedValue();

            _statusLabel.Text = "処理エラー";
            _statusLabel.ForeColor = Color.DarkRed;

            MessageBox.Show(
                this,
                ex.Message,
                "オンライン編集エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _writeInProgress = false;
            _valueInput.Enabled = _addressVerified;
        }
    }

    private void RestoreLastConfirmedValue()
    {
        _updating = true;

        try
        {
            _valueInput.Value = _lastConfirmedValue;
        }
        finally
        {
            _updating = false;
        }
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        RefreshFromGame();
    }

    /// <summary>
    /// ゲーム内の現在値を再取得する。
    /// 再取得時は書き込みを発生させない。
    /// </summary>
    public bool RefreshFromGame()
    {
        if (!_addressVerified)
        {
            _statusLabel.Text = "アドレス未検証";
            return false;
        }

        if (_writeInProgress)
            return false;

        if (!_service.TryRead(
            _address,
            _width,
            out byte[] data,
            out string message))
        {
            _statusLabel.Text = "読み取り失敗";
            _statusLabel.ForeColor = Color.DarkRed;

            MessageBox.Show(
                this,
                message,
                "オンライン読み取りエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        if (data.Length != _width)
        {
            _statusLabel.Text = "読み取りサイズ不一致";
            return false;
        }

        uint value = _width switch
        {
            1 => data[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(data),
            4 => BinaryPrimitives.ReadUInt32LittleEndian(data),
            _ => throw new InvalidOperationException("未対応のデータ幅です。")
        };

        _updating = true;

        try
        {
            _valueInput.Value = value;
            _lastConfirmedValue = value;
        }
        finally
        {
            _updating = false;
        }

        _statusLabel.Text = "ゲーム値取得済み";
        _statusLabel.ForeColor = Color.DarkGreen;

        return true;
    }

    /// <summary>
    /// 読み込み済みの値を画面へ反映する。
    /// この操作ではゲームへの書き込みを行わない。
    /// </summary>
    public void SetDisplayValue(uint value)
    {
        ulong maxValue = _width switch
        {
            1 => byte.MaxValue,
            2 => ushort.MaxValue,
            4 => uint.MaxValue,
            _ => 0
        };

        if (value > maxValue)
            throw new ArgumentOutOfRangeException(nameof(value));

        _updating = true;

        try
        {
            _valueInput.Value = value;
            _lastConfirmedValue = value;
        }
        finally
        {
            _updating = false;
        }
    }

    public void SetEditingEnabled(bool enabled)
    {
        bool actual = enabled && _addressVerified;
        _valueInput.Enabled = actual;
        _refreshButton.Enabled = actual;
    }
}