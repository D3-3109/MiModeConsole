# Redmi Book 模式控制台 (MiModeConsole)

小米 / Redmi 笔记本(Bitland MIFS 平台)性能模式切换 + 风扇 / 温度实时监控小工具。

适用于 **Redmi Book Pro 16 2026**(主板 TM2425,Intel Panther Lake / Bitland MIFS v2 固件,开发与实测机型)。同世代小米笔记本理论上协议同源,未经测试。

## 功能

- **一键切换性能模式**:安静 / 均衡 / 极速 / 狂暴(写入后自动回读校验)
- **实时监控**:CPU 温度、次级温度、双风扇转速,每秒刷新
- 单文件 exe(约 500 KB),启动仅需一次 UAC

## 技术原理

### 性能模式切换(写)

走小米官方 WMI 通道(与小米电脑管家同一条路径):

- 命名空间 `root\WMI`,类 `MICommonInterface`
  (GUID `B60BFB48-3E5B-49E4-A0E9-8CFFE1B3434B`,实例 `ACPI\PNP0C14\MIFS_0`)
- 方法 `MiInterface(InData[32], OutData[30], ReturnCode)`
- InData(小端):`fun1`(0xFA00=GET / 0xFB00=SET)+ `fun2`(0x0800=性能模式)+ `fun3`(模式码)+ `fun4`
- OutData:首 16 位为状态码 SGER,`0x8000` 表示成功;GET 时 `FRD0` 低字节返回当前模式码

| 模式码 | 含义 |
|---|---|
| 0x02 | 安静 |
| 0x03 | 均衡 |
| 0x04 | 极速 |
| 0x09 / 0x0A | 狂暴 |

### 风扇 / 温度读取(只读)

通过 [NBFC](https://github.com/hirschmann/nbfc) 附带的 `ec-probe.exe` 读取 EC RAM(**仅读,不写**):

| 寄存器 | 含义 |
|---|---|
| 0x69 / 0x6A | 风扇 1 转速,u16 小端(RPM) |
| 0x6B / 0x6C | 风扇 2 转速,u16 小端(RPM) |
| 0x0B | CPU 温度(°C) |
| 0x0F | 次级温度(°C) |

### 已知行为(2026 款实测)

- 风扇约 **44 °C 启动**,降到 **37~38 °C 停转**(迟滞带);低温下任何模式风扇都不转
- 模式切换只改变 EC 内置风扇曲线,**这一代 EC 固件不开放 OS 层转速直控**——本工具就是官方通道的上层封装,不做任何非官方 EC 写入
- **功耗限制分两层(实测 + HWiNFO 对照)**:
  - **静态 RAPL 上限**(MSR 0x610,WinRing0 只读采样):PL1/PL2 恒为 **85.0 / 95.0 W**,闲置/满载/四模式均不动——MIFS/QFAN 通道不碰它;MSR 0x614 显示 SKU 基础 TDP=25W,cTDP 级别=3
  - **动态 PL1**(Intel DTT 自适应策略,即 HWiNFO64 显示的那个):随模式变化——安静≈25W(恰等于 SKU TDP)、极速/狂暴≈70W、均衡最高可到 85W(自适应,受热状态调制)。DTT 在 MSR 之外的自适应机制中执行,不经典 RAPL 寄存器不可见
  - 推论:极速/狂暴的持续 CPU 功耗(70W)反而低于均衡(≤85W),应为游戏档给 GPU 留供电/热余量、换取更稳的持续频率的策略
  - Linux 上无 DTT 驱动,xiaomi-book14-dkms 只能切模式后手写 RAPL(15/35/45/61W),互为印证

## 构建

需要 .NET 10 SDK:

```sh
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```

产物:`bin\Release\net10.0-windows\win-x64\publish\MiModeConsole.exe`

## 使用

1. 双击 `MiModeConsole.exe`(内置 manifest,弹一次 UAC)
2. 需要同机安装过 [NBFC](https://github.com/hirschmann/nbfc)(本工具借用其 `ec-probe.exe` 读 EC;路径硬编码在 `Program.cs` 的 `EcProbe` 常量,可自行修改)

## 免责声明

模式切换走厂商官方 WMI 接口,EC 读取为只读操作,但不排除固件差异带来的未知行为。使用风险自负。

## 致谢

- [wolf109909/xiaomi-book14-dkms](https://github.com/wolf109909/xiaomi-book14-dkms) — MIFS WMI 协议与 EC 布局逆向参考
- [hirschmann/nbfc](https://github.com/hirschmann/nbfc) — ec-probe 工具
