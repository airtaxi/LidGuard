using System.Runtime.InteropServices;
using System.Text;

namespace LidGuard.Power;

internal static partial class MacOSAppleSiliconHumanInterfaceDeviceTemperatureSensor
{
    private const string CoreFoundationLibraryPath = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string InputOutputKitLibraryPath = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const uint CoreFoundationStringEncodingUtf8 = 0x08000100;
    private const int CoreFoundationNumberIntType = 9;
    private const int AppleVendorUsagePage = 0xff00;
    private const int AppleVendorTemperatureSensorUsage = 0x0005;
    private const long HumanInterfaceDeviceTemperatureEventType = 15;
    private const long HumanInterfaceDeviceTemperatureEventField = HumanInterfaceDeviceTemperatureEventType << 16;
    private const int ProductNameBufferLength = 256;
    private static readonly object s_coreFoundationExportGate = new();
    private static IntPtr s_coreFoundationLibraryHandle;
    private static IntPtr s_coreFoundationDictionaryKeyCallbacks;
    private static IntPtr s_coreFoundationDictionaryValueCallbacks;

    public static IEnumerable<double> ReadProcessorTemperaturesCelsius()
    {
        try { return ReadProcessorTemperaturesCelsiusCore(); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException) { return []; }
    }

    private static unsafe IEnumerable<double> ReadProcessorTemperaturesCelsiusCore()
    {
        var temperatures = new List<double>();
        var primaryUsagePageKey = IntPtr.Zero;
        var primaryUsageKey = IntPtr.Zero;
        var primaryUsagePageValue = IntPtr.Zero;
        var primaryUsageValue = IntPtr.Zero;
        var productKey = IntPtr.Zero;
        var matchingDictionary = IntPtr.Zero;
        var client = IntPtr.Zero;
        var services = IntPtr.Zero;

        try
        {
            primaryUsagePageKey = CreateCoreFoundationString("PrimaryUsagePage");
            primaryUsageKey = CreateCoreFoundationString("PrimaryUsage");
            productKey = CreateCoreFoundationString("Product");
            if (primaryUsagePageKey == IntPtr.Zero || primaryUsageKey == IntPtr.Zero || productKey == IntPtr.Zero) return temperatures;

            var usagePage = AppleVendorUsagePage;
            var usage = AppleVendorTemperatureSensorUsage;
            primaryUsagePageValue = CoreFoundationNumberCreate(IntPtr.Zero, CoreFoundationNumberIntType, ref usagePage);
            primaryUsageValue = CoreFoundationNumberCreate(IntPtr.Zero, CoreFoundationNumberIntType, ref usage);
            if (primaryUsagePageValue == IntPtr.Zero || primaryUsageValue == IntPtr.Zero) return temperatures;

            if (!TryGetCoreFoundationDictionaryCallbacks(out var dictionaryKeyCallbacks, out var dictionaryValueCallbacks)) return temperatures;

            var keys = stackalloc IntPtr[] { primaryUsagePageKey, primaryUsageKey };
            var values = stackalloc IntPtr[] { primaryUsagePageValue, primaryUsageValue };
            matchingDictionary = CoreFoundationDictionaryCreate(
                IntPtr.Zero,
                keys,
                values,
                2,
                dictionaryKeyCallbacks,
                dictionaryValueCallbacks);
            if (matchingDictionary == IntPtr.Zero) return temperatures;

            client = HumanInterfaceDeviceEventSystemClientCreate(IntPtr.Zero);
            if (client == IntPtr.Zero) return temperatures;

            HumanInterfaceDeviceEventSystemClientSetMatching(client, matchingDictionary);
            services = HumanInterfaceDeviceEventSystemClientCopyServices(client);
            if (services == IntPtr.Zero) return temperatures;

            var serviceCount = CoreFoundationArrayGetCount(services);
            for (nint serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
            {
                var service = CoreFoundationArrayGetValueAtIndex(services, serviceIndex);
                if (service == IntPtr.Zero) continue;

                var temperature = ReadServiceProcessorTemperatureCelsius(service, productKey);
                if (temperature is not null) temperatures.Add(temperature.Value);
            }

            return temperatures;
        }
        finally
        {
            ReleaseCoreFoundationObject(services);
            ReleaseCoreFoundationObject(client);
            ReleaseCoreFoundationObject(matchingDictionary);
            ReleaseCoreFoundationObject(productKey);
            ReleaseCoreFoundationObject(primaryUsageValue);
            ReleaseCoreFoundationObject(primaryUsagePageValue);
            ReleaseCoreFoundationObject(primaryUsageKey);
            ReleaseCoreFoundationObject(primaryUsagePageKey);
        }
    }

    private static double? ReadServiceProcessorTemperatureCelsius(IntPtr service, IntPtr productKey)
    {
        var productNameReference = IntPtr.Zero;
        var eventReference = IntPtr.Zero;

        try
        {
            productNameReference = HumanInterfaceDeviceServiceClientCopyProperty(service, productKey);
            if (productNameReference == IntPtr.Zero) return null;

            var productName = GetCoreFoundationString(productNameReference);
            if (!IsProcessorTemperatureProduct(productName)) return null;

            eventReference = HumanInterfaceDeviceServiceClientCopyEvent(
                service,
                HumanInterfaceDeviceTemperatureEventType,
                0,
                0);
            if (eventReference == IntPtr.Zero) return null;

            var temperatureCelsius = HumanInterfaceDeviceEventGetFloatValue(eventReference, HumanInterfaceDeviceTemperatureEventField);
            return temperatureCelsius > 0 && temperatureCelsius < 150 ? temperatureCelsius : null;
        }
        finally
        {
            ReleaseCoreFoundationObject(eventReference);
            ReleaseCoreFoundationObject(productNameReference);
        }
    }

    private static bool IsProcessorTemperatureProduct(string productName)
        => productName.Contains("PMU tdie", StringComparison.OrdinalIgnoreCase)
            || productName.Contains("pACC", StringComparison.OrdinalIgnoreCase)
            || productName.Contains("eACC", StringComparison.OrdinalIgnoreCase)
            || productName.Contains("CPU", StringComparison.OrdinalIgnoreCase);

    private static unsafe IntPtr CreateCoreFoundationString(string text)
    {
        var byteCount = Encoding.UTF8.GetByteCount(text);
        var buffer = stackalloc byte[byteCount + 1];
        Encoding.UTF8.GetBytes(text, new Span<byte>(buffer, byteCount));
        buffer[byteCount] = 0;
        return CoreFoundationStringCreateWithCString(IntPtr.Zero, buffer, CoreFoundationStringEncodingUtf8);
    }

    private static unsafe string GetCoreFoundationString(IntPtr textReference)
    {
        var buffer = stackalloc byte[ProductNameBufferLength];
        if (CoreFoundationStringGetCString(textReference, buffer, ProductNameBufferLength, CoreFoundationStringEncodingUtf8) == 0) return string.Empty;

        return Marshal.PtrToStringUTF8((IntPtr)buffer) ?? string.Empty;
    }

    private static void ReleaseCoreFoundationObject(IntPtr nativeObject)
    {
        if (nativeObject != IntPtr.Zero) CoreFoundationRelease(nativeObject);
    }

    private static bool TryGetCoreFoundationDictionaryCallbacks(
        out IntPtr dictionaryKeyCallbacks,
        out IntPtr dictionaryValueCallbacks)
    {
        dictionaryKeyCallbacks = IntPtr.Zero;
        dictionaryValueCallbacks = IntPtr.Zero;

        try
        {
            lock (s_coreFoundationExportGate)
            {
                if (s_coreFoundationLibraryHandle == IntPtr.Zero) s_coreFoundationLibraryHandle = NativeLibrary.Load(CoreFoundationLibraryPath);
                if (s_coreFoundationDictionaryKeyCallbacks == IntPtr.Zero
                    && !NativeLibrary.TryGetExport(s_coreFoundationLibraryHandle, "kCFTypeDictionaryKeyCallBacks", out s_coreFoundationDictionaryKeyCallbacks))
                {
                    return false;
                }

                if (s_coreFoundationDictionaryValueCallbacks == IntPtr.Zero
                    && !NativeLibrary.TryGetExport(s_coreFoundationLibraryHandle, "kCFTypeDictionaryValueCallBacks", out s_coreFoundationDictionaryValueCallbacks))
                {
                    return false;
                }

                dictionaryKeyCallbacks = s_coreFoundationDictionaryKeyCallbacks;
                dictionaryValueCallbacks = s_coreFoundationDictionaryValueCallbacks;
                return dictionaryKeyCallbacks != IntPtr.Zero && dictionaryValueCallbacks != IntPtr.Zero;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException) { return false; }
    }

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFArrayGetCount")]
    private static partial nint CoreFoundationArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFArrayGetValueAtIndex")]
    private static partial IntPtr CoreFoundationArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFDictionaryCreate")]
    private static unsafe partial IntPtr CoreFoundationDictionaryCreate(
        IntPtr allocator,
        IntPtr* keys,
        IntPtr* values,
        nint valueCount,
        IntPtr keyCallbacks,
        IntPtr valueCallbacks);

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFNumberCreate")]
    private static partial IntPtr CoreFoundationNumberCreate(IntPtr allocator, int numberType, ref int value);

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFRelease")]
    private static partial void CoreFoundationRelease(IntPtr nativeObject);

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFStringCreateWithCString")]
    private static unsafe partial IntPtr CoreFoundationStringCreateWithCString(IntPtr allocator, byte* text, uint encoding);

    [LibraryImport(CoreFoundationLibraryPath, EntryPoint = "CFStringGetCString")]
    private static unsafe partial byte CoreFoundationStringGetCString(IntPtr text, byte* buffer, nint bufferSize, uint encoding);

    [LibraryImport(InputOutputKitLibraryPath, EntryPoint = "IOHIDEventGetFloatValue")]
    private static partial double HumanInterfaceDeviceEventGetFloatValue(IntPtr eventReference, long field);

    [LibraryImport(InputOutputKitLibraryPath, EntryPoint = "IOHIDEventSystemClientCopyServices")]
    private static partial IntPtr HumanInterfaceDeviceEventSystemClientCopyServices(IntPtr client);

    [LibraryImport(InputOutputKitLibraryPath, EntryPoint = "IOHIDEventSystemClientCreate")]
    private static partial IntPtr HumanInterfaceDeviceEventSystemClientCreate(IntPtr allocator);

    [LibraryImport(InputOutputKitLibraryPath, EntryPoint = "IOHIDEventSystemClientSetMatching")]
    private static partial int HumanInterfaceDeviceEventSystemClientSetMatching(IntPtr client, IntPtr matching);

    [LibraryImport(InputOutputKitLibraryPath, EntryPoint = "IOHIDServiceClientCopyEvent")]
    private static partial IntPtr HumanInterfaceDeviceServiceClientCopyEvent(
        IntPtr service,
        long eventType,
        int options,
        long timeout);

    [LibraryImport(InputOutputKitLibraryPath, EntryPoint = "IOHIDServiceClientCopyProperty")]
    private static partial IntPtr HumanInterfaceDeviceServiceClientCopyProperty(IntPtr service, IntPtr key);
}
