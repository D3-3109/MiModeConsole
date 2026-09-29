using System.Diagnostics;
using System.Management;

namespace MiFanTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const string EcProbe = @"C:\Program Files (x86)\NoteBook FanControl\ec-probe.exe";

    private static readonly (byte Code, string Name)[] Modes =
    [
        (0x02, "安静"), (0x03, "均衡"), (0x04, "极速"), (0x09, "狂暴"),
    ];

    private readonly ManagementObject? _instance;
    private readonly Label _modeLabel = new();
    private readonly Label _cpuValue = new();
    private readonly Label _t2Value = new();
    private readonly Label _fan1Value = new();
    private readonly Label _fan2Value = new();
    private readonly Label _status = new();
    private readonly Button[] _buttons = new Button[Modes.Length];
    private readonly Font _valueFont = new("Microsoft YaHei UI", 17F, FontStyle.Bold);
    private int _currentMode = -1;
    private bool _busy;
    private int _tick;

    public MainForm()
    {
        Text = "Redmi Book 模式控制台";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 520);
        Font = new Font("Microsoft YaHei UI", 9.75F);
        BackColor = Color.White;

        try { _instance = FindMifsInstance(); }
        catch (Exception ex)
        {
            _instance = null;
            _status.Text = "WMI 初始化失败:" + ex.Message;
        }

        _modeLabel.SetBounds(28, 22, 584, 56);
        _modeLabel.Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
        _modeLabel.ForeColor = Color.FromArgb(32, 33, 36);
        _modeLabel.Text = "当前模式:获取中…";
        Controls.Add(_modeLabel);

        var flow = new FlowLayoutPanel
        {
            Location = new Point(28, 96),
            Size = new Size(584, 70),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        for (int i = 0; i < Modes.Length; i++)
        {
            var b = new Button
            {
                Text = Modes[i].Name,
                Tag = Modes[i].Code,
                Size = new Size(128, 58),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                BackColor = Color.FromArgb(245, 246, 248),
                ForeColor = Color.FromArgb(51, 51, 51),
                Margin = new Padding(0, 0, 12, 0),
                Enabled = _instance is not null,
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(220, 224, 229);
            b.Click += async (_, _) => await SwitchModeAsync((byte)b.Tag!);
            _buttons[i] = b;
            flow.Controls.Add(b);
        }
        Controls.Add(flow);

        var group = new GroupBox { Text = "实时监控", Bounds = new Rectangle(28, 182, 584, 250) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 8, 10, 6),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _cpuValue = AddCell(grid, "CPU 温度", 0, 0);
        _t2Value = AddCell(grid, "次级温度", 1, 0);
        _fan1Value = AddCell(grid, "风扇 1", 0, 1);
        _fan2Value = AddCell(grid, "风扇 2", 1, 1);
        group.Controls.Add(grid);
        Controls.Add(group);

        _status.SetBounds(30, 452, 584, 48);
        _status.ForeColor = Color.Gray;
        _status.Text = "就绪";
        Controls.Add(_status);

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += async (_, _) => await PollAsync();
        timer.Start();
    }

    private Label AddCell(TableLayoutPanel grid, string caption, int col, int row)
    {
        var cell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(4),
        };
        cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cap = new Label
        {
            Text = caption,
            Dock = DockStyle.Top,
            ForeColor = Color.Gray,
            AutoSize = false,
            Height = 34,
            Font = new Font("Microsoft YaHei UI", 10.5F),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var val = new Label
        {
            Text = "…",
            Dock = DockStyle.Fill,
            Font = _valueFont,
            ForeColor = Color.FromArgb(32, 33, 36),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        cell.Controls.Add(cap);
        cell.Controls.Add(val);
        grid.Controls.Add(cell, col, row);
        return val;
    }

    private static ManagementObject? FindMifsInstance()
    {
        using var cls = new ManagementClass("root\\WMI", "MICommonInterface", null);
        foreach (ManagementObject o in cls.GetInstances())
        {
            return o;
        }
        return null;
    }

    private byte[] InvokeMifs(ushort fun1, ushort fun2, ushort fun3 = 0)
    {
        if (_instance is null) throw new InvalidOperationException("WMI 实例不可用");
        var inp = _instance.GetMethodParameters("MiInterface");
        var buf = new byte[32];
        buf[0] = (byte)fun1; buf[1] = (byte)(fun1 >> 8);
        buf[2] = (byte)fun2; buf[3] = (byte)(fun2 >> 8);
        buf[4] = (byte)fun3; buf[5] = (byte)(fun3 >> 8);
        inp["InData"] = buf;
        using var outp = _instance.InvokeMethod("MiInterface", inp, null);
        var o = (byte[])outp["OutData"]!;
        int sger = o[0] | (o[1] << 8);
        if (sger != 0x8000) throw new InvalidOperationException($"EC 返回错误 SGER=0x{sger:X4}");
        return o;
    }

    private byte GetMode()
    {
        var o = InvokeMifs(0xFA00, 0x0800);
        return (byte)(o[4] | (o[5] << 8));
    }

    private static int[] ReadEc()
    {
        var psi = new ProcessStartInfo
        {
            FileName = EcProbe,
            Arguments = "dump",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ec-probe 启动失败");
        string text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(3000);

        int[] regs = new int[256];
        foreach (string rawLine in text.Split('\n'))
        {
            int bar = rawLine.IndexOf('|');
            if (bar < 2) continue;
            string head = rawLine[..bar].Trim();
            if (head.Length != 2) continue;
            int addr;
            try { addr = Convert.ToInt32(head, 16); } catch { continue; }
            string[] parts = rawLine[(bar + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length && addr + i < 256; i++)
            {
                try { regs[addr + i] = Convert.ToInt32(parts[i], 16); } catch { }
            }
        }

        return
        [
            regs[0x69] | (regs[0x6A] << 8),
            regs[0x6B] | (regs[0x6C] << 8),
            regs[0x0B],
            regs[0x0F],
        ];
    }

    private async Task SwitchModeAsync(byte code)
    {
        if (_busy) return;
        _busy = true;
        _status.Text = "切换中…";
        try
        {
            await Task.Run(() =>
            {
                InvokeMifs(0xFB00, 0x0800, code);
                byte rb = GetMode();
                if (rb != code)
                {
                    throw new InvalidOperationException($"回读 0x{rb:X2} != 0x{code:X2}");
                }
            });
            _currentMode = code;
            UpdateModeUi();
            _status.Text = $"已切换到 {NameOf(code)}  {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _status.Text = "切换失败:" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PollAsync()
    {
        if (_busy) return;
        _tick++;
        try
        {
            if (_currentMode < 0 || _tick % 5 == 1)
            {
                byte m = await Task.Run(GetMode);
                if (m != _currentMode)
                {
                    _currentMode = m;
                    UpdateModeUi();
                }
            }

            int[] v = await Task.Run(ReadEc);
            _cpuValue.Text = v[2] + " °C";
            _t2Value.Text = v[3] + " °C";
            _fan1Value.Text = v[0] + " RPM";
            _fan2Value.Text = v[1] + " RPM";
        }
        catch (Exception ex)
        {
            _status.Text = "读取失败:" + ex.Message;
        }
    }

    private void UpdateModeUi()
    {
        string name = $"未知 (0x{_currentMode:X2})";
        for (int i = 0; i < Modes.Length; i++)
        {
            bool active = Modes[i].Code == _currentMode;
            if (active) name = Modes[i].Name;
            _buttons[i].BackColor = active ? Color.FromArgb(255, 105, 0) : Color.FromArgb(245, 246, 248);
            _buttons[i].ForeColor = active ? Color.White : Color.FromArgb(51, 51, 51);
        }
        _modeLabel.Text = "当前模式:" + name;
    }

    private static string NameOf(byte code)
    {
        foreach (var m in Modes)
        {
            if (m.Code == code) return m.Name;
        }
        return $"未知 0x{code:X2}";
    }
}
