using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Corkboard.Platforms.Abstractions;

namespace Corkboard.Platforms.Windows;

/// <summary>
///     枚举 DirectShow 的 video input 设备（裸 COM，不引任何第三方包）。
///     <para>
///         走的是系统「设备枚举器」（<c>CLSID_SystemDeviceEnum</c>）→ 按 video input 类别
///         （<c>CLSID_VideoInputDeviceCategory</c>）拿 moniker → 读 moniker 的 <c>FriendlyName</c>。
///         这样拿到的名字与系统相机应用、浏览器看到的是同一份，
///         而且<b>序号</b>正好就是 <c>VideoCapture(index, VideoCaptureAPIs.DSHOW)</c> 用的那个序号
///         （两者都按同一类别的枚举顺序，所以下面按「有效名字」递增计数）。
///     </para>
///     <para>
///         枚举失败（没摄像头、COM 报错）一律返回空列表，不把异常抛给界面。
///     </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCameraDeviceCatalog
{
    /// <summary>DirectShow 的 video input 设备类别。</summary>
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");

    /// <summary>
    ///     取当前可用的相机。失败返回空列表。
    /// </summary>
    public Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);

        var devices = new List<PlatformCameraDevice>();
        object? systemDeviceEnumerator = null;
        IEnumMoniker? enumerator = null;
        try
        {
            systemDeviceEnumerator = new SystemDeviceEnumerator();
            var createDeviceEnumerator = (ICreateDeviceEnumerator)systemDeviceEnumerator;
            var category = VideoInputDeviceCategory;
            if (createDeviceEnumerator.CreateClassEnumerator(ref category, out enumerator, 0) != 0 ||
                enumerator is null)
            {
                return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
            }

            var monikers = new IMoniker[1];
            var captureIndex = 0;
            while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moniker = monikers[0];
                try
                {
                    var name = GetFriendlyName(moniker);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // 序号必须与「有效设备」的序号一致：没有名字的 moniker 不占号，
                    // 否则 OpenCV 按序号打开时会错到别的设备上。
                    devices.Add(new PlatformCameraDevice($"dshow:{captureIndex}", name, captureIndex));
                    captureIndex++;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(moniker);
                    monikers[0] = null!;
                }
            }
        }
        catch (COMException)
        {
            devices.Clear();
        }
        finally
        {
            if (enumerator is not null)
                Marshal.FinalReleaseComObject(enumerator);
            if (systemDeviceEnumerator is not null)
                Marshal.FinalReleaseComObject(systemDeviceEnumerator);
        }

        return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
    }

    /// <summary>从设备 moniker 读 <c>FriendlyName</c>；读不到返回 <see langword="null" />。</summary>
    [SupportedOSPlatform("windows")]
    private static string? GetFriendlyName(IMoniker moniker)
    {
        var propertyBagId = typeof(IPropertyBag).GUID;
        object? propertyBagObject = null;
        try
        {
            moniker.BindToStorage(null!, null!, ref propertyBagId, out propertyBagObject);
            if (propertyBagObject is not IPropertyBag propertyBag ||
                propertyBag.Read("FriendlyName", out var value, IntPtr.Zero) != 0)
            {
                return null;
            }

            return value as string;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (propertyBagObject is not null && Marshal.IsComObject(propertyBagObject))
                Marshal.FinalReleaseComObject(propertyBagObject);
        }
    }

    /// <summary><c>CLSID_SystemDeviceEnum</c>：系统设备枚举器（DirectShow 设备都从这里按类别取）。</summary>
    [ComImport]
    [Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")]
    private class SystemDeviceEnumerator
    {
    }

    /// <summary><c>ICreateDeviceEnumerator</c>：按类别拿 moniker 枚举器。</summary>
    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDeviceEnumerator
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid category, out IEnumMoniker? enumerator, int flags);
    }

    /// <summary>moniker 的属性包：设备名就挂在 <c>FriendlyName</c> 上。</summary>
    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value,
            IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}