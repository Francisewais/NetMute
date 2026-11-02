using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NetMute
{
    // Windows Filtering Platform declarations, from fwpmu.h / fwpmtypes.h / fwptypes.h.
    // Only what NetMute uses. Sequential layout lets the marshaller work out the padding
    // for each architecture; SelfTest compares the result with the known x64 layout.

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWP_BYTE_BLOB
    {
        public uint size;
        public IntPtr data;
    }

    // Also stands in for FWP_CONDITION_VALUE0. Both are a type tag and a union whose largest
    // member is a pointer (64-bit integers are held by pointer), so an IntPtr fits everywhere.
    [StructLayout(LayoutKind.Sequential)]
    internal struct FWP_VALUE0
    {
        public uint type;
        public IntPtr value;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_DISPLAY_DATA0
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string name;
        [MarshalAs(UnmanagedType.LPWStr)] public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_PROVIDER0
    {
        public Guid providerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public FWP_BYTE_BLOB providerData;
        public IntPtr serviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;
        public uint matchType;
        public FWP_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public IntPtr providerKey;
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public IntPtr filterCondition;
        public FWPM_ACTION0 action;
        // union { UINT64 rawContext; GUID providerContextKey; }: 16 bytes, aligned to 8.
        public ulong rawContext;
        public ulong rawContextHigh;
        public IntPtr reserved;
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    internal static class WfpNative
    {
        public const uint RPC_C_AUTHN_WINNT = 10;

        public const uint FWP_EMPTY = 0;
        public const uint FWP_UINT16 = 2;
        public const uint FWP_UINT32 = 3;
        public const uint FWP_BYTE_BLOB_TYPE = 12;

        public const uint FWP_MATCH_EQUAL = 0;
        public const uint FWP_MATCH_FLAGS_ALL_SET = 6;
        public const uint FWP_MATCH_FLAGS_NONE_SET = 8;

        public const uint FWP_ACTION_BLOCK = 0x00001001;
        public const uint FWP_CONDITION_FLAG_IS_LOOPBACK = 0x00000001;

        public const uint FWPM_PROVIDER_FLAG_PERSISTENT = 0x00000001;
        public const uint FWPM_SUBLAYER_FLAG_PERSISTENT = 0x00000001;
        public const uint FWPM_FILTER_FLAG_PERSISTENT = 0x00000001;

        public const uint FWP_E_FILTER_NOT_FOUND = 0x80320003;
        public const uint FWP_E_PROVIDER_NOT_FOUND = 0x80320005;
        public const uint FWP_E_SUBLAYER_NOT_FOUND = 0x80320007;
        public const uint FWP_E_ALREADY_EXISTS = 0x80320009;

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        public static extern uint FwpmEngineOpen0(
            string serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engine);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmEngineClose0(IntPtr engine);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmTransactionCommit0(IntPtr engine);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmTransactionAbort0(IntPtr engine);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmProviderAdd0(IntPtr engine, [In] ref FWPM_PROVIDER0 provider, IntPtr sd);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmProviderDeleteByKey0(IntPtr engine, [In] ref Guid key);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmSubLayerAdd0(IntPtr engine, [In] ref FWPM_SUBLAYER0 subLayer, IntPtr sd);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine, [In] ref Guid key);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterAdd0(IntPtr engine, [In] ref FWPM_FILTER0 filter, IntPtr sd, out ulong id);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterDeleteById0(IntPtr engine, ulong id);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterCreateEnumHandle0(IntPtr engine, IntPtr enumTemplate, out IntPtr enumHandle);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterEnum0(
            IntPtr engine, IntPtr enumHandle, uint requested, out IntPtr entries, out uint returned);

        [DllImport("fwpuclnt.dll")]
        public static extern uint FwpmFilterDestroyEnumHandle0(IntPtr engine, IntPtr enumHandle);

        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        public static extern uint FwpmGetAppIdFromFileName0(string fileName, out IntPtr appId);

        [DllImport("fwpuclnt.dll")]
        public static extern void FwpmFreeMemory0(ref IntPtr p);

        // Empty when the structs above match the x64 layout of the Windows headers.
        public static string LayoutProblems()
        {
            if (IntPtr.Size != 8)
                return "";

            StringBuilder problems = new StringBuilder();
            Size(problems, typeof(FWPM_PROVIDER0), 64);
            Offset(problems, typeof(FWPM_PROVIDER0), "flags", 32);
            Offset(problems, typeof(FWPM_PROVIDER0), "providerData", 40);
            Offset(problems, typeof(FWPM_PROVIDER0), "serviceName", 56);

            Size(problems, typeof(FWPM_SUBLAYER0), 72);
            Offset(problems, typeof(FWPM_SUBLAYER0), "flags", 32);
            Offset(problems, typeof(FWPM_SUBLAYER0), "providerKey", 40);
            Offset(problems, typeof(FWPM_SUBLAYER0), "providerData", 48);
            Offset(problems, typeof(FWPM_SUBLAYER0), "weight", 64);

            Size(problems, typeof(FWPM_FILTER_CONDITION0), 40);
            Offset(problems, typeof(FWPM_FILTER_CONDITION0), "matchType", 16);
            Offset(problems, typeof(FWPM_FILTER_CONDITION0), "conditionValue", 24);

            Size(problems, typeof(FWPM_FILTER0), 200);
            Offset(problems, typeof(FWPM_FILTER0), "displayData", 16);
            Offset(problems, typeof(FWPM_FILTER0), "flags", 32);
            Offset(problems, typeof(FWPM_FILTER0), "providerKey", 40);
            Offset(problems, typeof(FWPM_FILTER0), "providerData", 48);
            Offset(problems, typeof(FWPM_FILTER0), "layerKey", 64);
            Offset(problems, typeof(FWPM_FILTER0), "subLayerKey", 80);
            Offset(problems, typeof(FWPM_FILTER0), "weight", 96);
            Offset(problems, typeof(FWPM_FILTER0), "numFilterConditions", 112);
            Offset(problems, typeof(FWPM_FILTER0), "filterCondition", 120);
            Offset(problems, typeof(FWPM_FILTER0), "action", 128);
            Offset(problems, typeof(FWPM_FILTER0), "rawContext", 152);
            Offset(problems, typeof(FWPM_FILTER0), "reserved", 168);
            Offset(problems, typeof(FWPM_FILTER0), "filterId", 176);
            Offset(problems, typeof(FWPM_FILTER0), "effectiveWeight", 184);
            return problems.ToString();
        }

        private static void Size(StringBuilder problems, Type type, int expected)
        {
            int actual = Marshal.SizeOf(type);
            if (actual != expected)
                problems.AppendFormat("{0} size {1}, expected {2}; ", type.Name, actual, expected);
        }

        private static void Offset(StringBuilder problems, Type type, string field, int expected)
        {
            int actual = Marshal.OffsetOf(type, field).ToInt32();
            if (actual != expected)
                problems.AppendFormat("{0}.{1} at {2}, expected {3}; ", type.Name, field, actual, expected);
        }
    }
}
