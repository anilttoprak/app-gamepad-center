Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { 
    $_.Name -eq 'AsTask' -and 
    $_.GetParameters().Count -eq 1 -and 
    $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' 
} | Select-Object -First 1

function Await($asyncOp, $type) {
    $m = $asTaskGeneric.MakeGenericMethod($type)
    $task = $m.Invoke($null, @($asyncOp))
    return $task.GetAwaiter().GetResult()
}

[Windows.Devices.Bluetooth.BluetoothLEDevice, Windows.Devices.Bluetooth, ContentType = WindowsRuntime] | Out-Null
[Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceServicesResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime] | Out-Null
[Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicsResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime] | Out-Null
[Windows.Devices.Bluetooth.GenericAttributeProfile.GattReadResult, Windows.Devices.Bluetooth, ContentType = WindowsRuntime] | Out-Null

$mac = [Convert]::ToUInt64("78862EE89F4C", 16)
$devOp = [Windows.Devices.Bluetooth.BluetoothLEDevice]::FromBluetoothAddressAsync($mac)
$dev = Await $devOp ([Windows.Devices.Bluetooth.BluetoothLEDevice])

if ($dev) {
    $svcOp = $dev.GetGattServicesForUuidAsync([Windows.Devices.Bluetooth.GenericAttributeProfile.GattServiceUuids]::Battery)
    $svcRes = Await $svcOp ([Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceServicesResult])
    
    foreach ($s in $svcRes.Services) {
        $chOp = $s.GetCharacteristicsForUuidAsync([Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicUuids]::BatteryLevel)
        $chRes = Await $chOp ([Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicsResult])
        foreach ($c in $chRes.Characteristics) {
            $rdOp = $c.ReadValueAsync()
            $rdRes = Await $rdOp ([Windows.Devices.Bluetooth.GenericAttributeProfile.GattReadResult])
            if ($rdRes.Status -eq 'Success') {
                $bytes = [System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions]::ToArray($rdRes.Value)
                Write-Host ">>> DOGRULANAN GERCEK PIL SEVIYESI: %$($bytes[0]) <<<" -ForegroundColor Green
            }
        }
    }
}
