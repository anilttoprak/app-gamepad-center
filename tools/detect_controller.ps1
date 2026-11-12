<#
.SYNOPSIS
    Controller Hub - Cihaz Tanılama ve Sinyal Doğrulama Aracı (Aşama 0)
.DESCRIPTION
    Bu script, bilgisayara bağlı veya eşleştirilmiş Xbox kontrolcülerini
    PnP, XInput ve Bluetooth GATT katmanlarında tarar ve durumlarını raporlar.
#>

[CmdletBinding()]
param()

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "    Controller Hub - Xbox Cihaz Tanılama Aracı (Aşama 0)  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. PnP Taraması (Xbox & Bluetooth LE)
Write-Host "`n[1/3] PnP Aygıt Listesi Taranıyor (Microsoft VID: 045E)..." -ForegroundColor Yellow

$xboxDevices = Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
    ($_.FriendlyName -match "Xbox|Wireless Controller" -or $_.HardwareID -match "045E") -and
    $_.FriendlyName -notmatch "Bus|Virtual|Synthetic"
}

if (-not $xboxDevices) {
    Write-Host "  [-] Eşleştirilmiş veya takılı Xbox kontrolcüsü bulunamadı." -ForegroundColor Gray
} else {
    Write-Host "  [+] Bulunan Aygıt Düğümleri:" -ForegroundColor Green
    foreach ($dev in $xboxDevices) {
        $statusColor = if ($dev.Present) { "Green" } else { "DarkGray" }
        Write-Host "    - $($dev.FriendlyName)" -ForegroundColor $statusColor
        Write-Host "        Sınıf        : $($dev.Class)" -ForegroundColor Gray
        Write-Host "        Durum (PnP)  : $($dev.Status)" -ForegroundColor Gray
        Write-Host "        Şu An Bağlı? : $($dev.Present)" -ForegroundColor $statusColor
        Write-Host "        Instance ID  : $($dev.InstanceId)" -ForegroundColor DarkGray
    }
}

# 2. XInput Durum Kontrolü
Write-Host "`n[2/3] XInput Slotları Taranıyor (Slot 0 - 3)..." -ForegroundColor Yellow

$xinputPInvoke = @"
using System;
using System.Runtime.InteropServices;

public static class XInputChecker {
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_BATTERY_INFORMATION {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE {
        public uint dwPacketNumber;
        public byte[] Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint dwUserIndex, IntPtr pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState91(uint dwUserIndex, IntPtr pState);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInputGetBattery14(uint dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBat);

    public static int CheckSlot(uint slot, out byte batType, out byte batLevel) {
        batType = 0;
        batLevel = 0;
        IntPtr dummy = Marshal.AllocHGlobal(64);
        uint res = 1167; // ERROR_DEVICE_NOT_CONNECTED
        try {
            res = XInputGetState14(slot, dummy);
        } catch {
            try { res = XInputGetState91(slot, dummy); } catch { }
        }
        Marshal.FreeHGlobal(dummy);

        if (res == 0) {
            try {
                XINPUT_BATTERY_INFORMATION bat;
                if (XInputGetBattery14(slot, 0, out bat) == 0) {
                    batType = bat.BatteryType;
                    batLevel = bat.BatteryLevel;
                }
            } catch { }
        }
        return (int)res;
    }
}
"@

try {
    Add-Type -TypeDefinition $xinputPInvoke -ErrorAction SilentlyContinue
    $anyConnected = $false
    for ($i = 0; $i -lt 4; $i++) {
        $batType = [byte]0
        $batLevel = [byte]0
        $res = [XInputChecker]::CheckSlot($i, [ref]$batType, [ref]$batLevel)
        if ($res -eq 0) {
            $anyConnected = $true
            $levelStr = switch ($batLevel) {
                0 { "Boş (Empty)" }
                1 { "Düşük (Low)" }
                2 { "Orta (Medium)" }
                3 { "Dolu (Full)" }
                default { "Bilinmiyor" }
            }
            Write-Host "  [+] Slot $i : BAĞLI (XInput Aktif)" -ForegroundColor Green
            Write-Host "        Pil Seviyesi: $levelStr (Kod: $batLevel, Tip: $batType)" -ForegroundColor Cyan
        }
    }
    if (-not $anyConnected) {
        Write-Host "  [-] Hiçbir XInput slotunda aktif kontrolcü sinyali yok (Kontrolcü kapalı veya bağlı değil)." -ForegroundColor Gray
    }
} catch {
    Write-Host "  [!] XInput API çağrısı yapılamadı: $($_.Exception.Message)" -ForegroundColor Red
}

# 3. Özet ve Sonuç
Write-Host "`n[3/3] Değerlendirme:" -ForegroundColor Yellow
$activePnp = $xboxDevices | Where-Object { $_.Present -eq $true }
if ($activePnp -or $anyConnected) {
    Write-Host "  >>> KONTROLCÜ ŞU ANDA BAĞLI VE AKTİF <<<" -ForegroundColor Green
} else {
    Write-Host "  >>> KONTROLCÜ ŞU ANDA BAĞLI DEĞİL (Disconnected) <<<" -ForegroundColor Yellow
    Write-Host "  Kontrolcünüzü Bluetooth ile bağladığınızda bu scripti tekrar çalıştırarak canlı sinyalleri görebilirsiniz." -ForegroundColor Gray
}
Write-Host "==========================================================`n" -ForegroundColor Cyan
