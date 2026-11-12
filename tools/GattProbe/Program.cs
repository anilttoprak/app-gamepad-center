using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

Console.WriteLine("==========================================================");
Console.WriteLine("  Xbox Controller - Dinamik Cihaz Arama & Pil Testi       ");
Console.WriteLine("==========================================================");

try
{
    // AQS filtresi: Bluetooth LE cihazları arasında adı Xbox veya Controller geçenler
    string aqs = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
    var devices = await DeviceInformation.FindAllAsync(aqs);

    Console.WriteLine($"Eşleştirilmiş Bluetooth LE Cihaz Sayısı: {devices.Count}");

    foreach (var d in devices)
    {
        if (d.Name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("Controller", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"\n[Bulunan Kontrolcü]");
            Console.WriteLine($"  Adı       : {d.Name}");
            Console.WriteLine($"  Cihaz ID  : {d.Id}");
            Console.WriteLine($"  Etkin mi  : {d.IsEnabled}");

            var bleDevice = await BluetoothLEDevice.FromIdAsync(d.Id);
            if (bleDevice != null)
            {
                Console.WriteLine($"  MAC       : {bleDevice.BluetoothAddress:X}");
                Console.WriteLine($"  Durum     : {bleDevice.ConnectionStatus}");

                var svcResult = await bleDevice.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
                if (svcResult.Status == GattCommunicationStatus.Success && svcResult.Services.Count > 0)
                {
                    var chResult = await svcResult.Services[0].GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
                    if (chResult.Status == GattCommunicationStatus.Success && chResult.Characteristics.Count > 0)
                    {
                        var read = await chResult.Characteristics[0].ReadValueAsync();
                        if (read.Status == GattCommunicationStatus.Success)
                        {
                            byte[] val = read.Value.ToArray();
                            Console.WriteLine($"  >>> OKUNAN PİL: %{val[0]} <<<");
                        }
                    }
                }
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Hata: {ex.Message}");
}
