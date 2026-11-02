using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using W = NetMute.WfpNative;

namespace NetMute
{
    internal sealed class BlockedApp
    {
        public string Path;
        public string AppId;
        public readonly List<ulong> FilterIds = new List<ulong>();
    }

    internal sealed class WfpException : Exception
    {
        public WfpException(uint code)
            : base(Describe(code))
        {
        }

        private static string Describe(uint code)
        {
            if (code == 5)
                return L.T("需要管理员权限。", "Administrator rights are required.");
            if (code == 1753 || code == 1722)
                return L.T(
                    "Windows 的 Base Filtering Engine 服务没有运行，请在“服务”里启动它。",
                    "The Windows Base Filtering Engine service is not running. Start it in Services.");
            return new Win32Exception(unchecked((int)code)).Message + string.Format(" (0x{0:X8})", code);
        }
    }

    // Block rules for single programs, kept in the Windows Filtering Platform: the engine
    // underneath Windows Firewall. Rules are persistent, so Windows keeps enforcing them after
    // NetMute exits and after a reboot, and they work with Windows Firewall turned off.
    //
    // A blocked program gets six filters:
    //   - outbound and inbound (IPv4 and IPv6): everything that is not loopback
    //   - outbound loopback (IPv4 and IPv6): only to local proxy ports
    //
    // The second group exists because a program can still reach the internet through a proxy
    // client on 127.0.0.1, which is a different, unblocked process. All other loopback traffic
    // has to keep working: steamwebhelper.exe talks to steam.exe over 127.0.0.1 on a dynamic
    // port, and Python and Java use loopback sockets internally.
    internal sealed class Firewall : IDisposable
    {
        // How NetMute recognises its own objects in WFP. Never change these, or new versions
        // lose track of the rules that older versions left behind.
        private static readonly Guid ProviderKey = new Guid("f7f54842-14ee-4280-9b2c-dc1357bf9d81");
        private static readonly Guid SublayerKey = new Guid("1fe188ff-ed79-4fdf-8b96-23184c296f29");

        // Highest priority. Permits in other sublayers cannot override a block made here.
        private const ushort SublayerWeight = 0xFFFF;

        private static readonly Guid LayerConnectV4 = new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82");
        private static readonly Guid LayerConnectV6 = new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
        private static readonly Guid LayerRecvAcceptV4 = new Guid("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
        private static readonly Guid LayerRecvAcceptV6 = new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f");

        private static readonly Guid ConditionAppId = new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971");
        private static readonly Guid ConditionFlags = new Guid("632ce23b-5167-435c-86d7-e903684aa80c");
        private static readonly Guid ConditionRemotePort = new Guid("c35a604d-d22b-4e1a-91b4-68f674ee674b");

        private IntPtr engine;
        private IntPtr providerKeyPtr;

        public Firewall()
        {
            Check(W.FwpmEngineOpen0(null, W.RPC_C_AUTHN_WINNT, IntPtr.Zero, IntPtr.Zero, out engine));
            providerKeyPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Guid)));
            Marshal.StructureToPtr(ProviderKey, providerKeyPtr, false);
        }

        public void Dispose()
        {
            if (engine != IntPtr.Zero)
            {
                W.FwpmEngineClose0(engine);
                engine = IntPtr.Zero;
            }
            if (providerKeyPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(providerKeyPtr);
                providerKeyPtr = IntPtr.Zero;
            }
        }

        public void Block(string path)
        {
            IntPtr appId;
            Check(W.FwpmGetAppIdFromFileName0(path, out appId));
            try
            {
                FWPM_FILTER_CONDITION0 program = new FWPM_FILTER_CONDITION0();
                program.fieldKey = ConditionAppId;
                program.matchType = W.FWP_MATCH_EQUAL;
                program.conditionValue.type = W.FWP_BYTE_BLOB_TYPE;
                program.conditionValue.value = appId;

                FWPM_FILTER_CONDITION0[] external =
                {
                    program,
                    FlagsCondition(W.FWP_MATCH_FLAGS_NONE_SET)
                };

                // Windows ORs consecutive conditions on the same field, so one filter covers
                // every port. SelfTest confirms this on the first and last port of the list.
                List<FWPM_FILTER_CONDITION0> proxied = new List<FWPM_FILTER_CONDITION0>();
                proxied.Add(program);
                proxied.Add(FlagsCondition(W.FWP_MATCH_FLAGS_ALL_SET));
                foreach (ushort port in LocalProxy.Ports())
                    proxied.Add(PortCondition(port));

                List<ulong> stale = FilterIdsFor(path, ReadBlob(appId));
                EnsureProviderAndSublayer();

                Transaction(delegate
                {
                    foreach (ulong id in stale)
                        DeleteFilter(id);
                    AddFilter(LayerConnectV4, path, external);
                    AddFilter(LayerConnectV6, path, external);
                    AddFilter(LayerRecvAcceptV4, path, external);
                    AddFilter(LayerRecvAcceptV6, path, external);
                    AddFilter(LayerConnectV4, path, proxied.ToArray());
                    AddFilter(LayerConnectV6, path, proxied.ToArray());
                });
            }
            finally
            {
                W.FwpmFreeMemory0(ref appId);
            }
        }

        public bool Unblock(string path)
        {
            List<ulong> ids = FilterIdsFor(path, TryGetAppId(path));
            if (ids.Count == 0)
                return false;
            Transaction(delegate
            {
                foreach (ulong id in ids)
                    DeleteFilter(id);
            });
            return true;
        }

        public bool IsBlocked(string path)
        {
            return FilterIdsFor(path, TryGetAppId(path)).Count > 0;
        }

        public List<BlockedApp> Blocked()
        {
            Dictionary<string, BlockedApp> apps = new Dictionary<string, BlockedApp>(StringComparer.OrdinalIgnoreCase);
            IntPtr enumHandle;
            Check(W.FwpmFilterCreateEnumHandle0(engine, IntPtr.Zero, out enumHandle));
            try
            {
                const uint PageSize = 512;
                uint returned;
                do
                {
                    IntPtr entries;
                    Check(W.FwpmFilterEnum0(engine, enumHandle, PageSize, out entries, out returned));
                    try
                    {
                        for (int i = 0; i < returned; i++)
                            Collect(Marshal.ReadIntPtr(entries, i * IntPtr.Size), apps);
                    }
                    finally
                    {
                        if (entries != IntPtr.Zero)
                            W.FwpmFreeMemory0(ref entries);
                    }
                }
                while (returned == PageSize);
            }
            finally
            {
                W.FwpmFilterDestroyEnumHandle0(engine, enumHandle);
            }
            return new List<BlockedApp>(apps.Values);
        }

        // Removes every trace of NetMute from WFP.
        public void RemoveEverything()
        {
            List<ulong> ids = new List<ulong>();
            foreach (BlockedApp app in Blocked())
                ids.AddRange(app.FilterIds);

            Transaction(delegate
            {
                foreach (ulong id in ids)
                    DeleteFilter(id);
            });

            Guid sublayer = SublayerKey;
            Guid provider = ProviderKey;
            CheckExcept(W.FwpmSubLayerDeleteByKey0(engine, ref sublayer), W.FWP_E_SUBLAYER_NOT_FOUND);
            CheckExcept(W.FwpmProviderDeleteByKey0(engine, ref provider), W.FWP_E_PROVIDER_NOT_FOUND);
        }

        private static void Collect(IntPtr filterPtr, Dictionary<string, BlockedApp> apps)
        {
            FWPM_FILTER0 filter = (FWPM_FILTER0)Marshal.PtrToStructure(filterPtr, typeof(FWPM_FILTER0));
            if (filter.subLayerKey != SublayerKey)
                return;

            string path = filter.displayData.description ?? "";
            string appId = ReadFilterAppId(filter) ?? path;

            BlockedApp app;
            if (!apps.TryGetValue(appId, out app))
            {
                app = new BlockedApp();
                app.Path = path;
                app.AppId = appId;
                apps[appId] = app;
            }
            app.FilterIds.Add(filter.filterId);
        }

        private static string ReadFilterAppId(FWPM_FILTER0 filter)
        {
            int size = Marshal.SizeOf(typeof(FWPM_FILTER_CONDITION0));
            for (int i = 0; i < filter.numFilterConditions; i++)
            {
                FWPM_FILTER_CONDITION0 condition = (FWPM_FILTER_CONDITION0)Marshal.PtrToStructure(
                    filter.filterCondition + i * size, typeof(FWPM_FILTER_CONDITION0));
                if (condition.fieldKey == ConditionAppId && condition.conditionValue.type == W.FWP_BYTE_BLOB_TYPE)
                    return ReadBlob(condition.conditionValue.value);
            }
            return null;
        }

        // An application id is a NUL-terminated UTF-16 NT path, such as
        // \device\harddiskvolume3\program files (x86)\steam\steam.exe
        private static string ReadBlob(IntPtr blobPtr)
        {
            FWP_BYTE_BLOB blob = (FWP_BYTE_BLOB)Marshal.PtrToStructure(blobPtr, typeof(FWP_BYTE_BLOB));
            return Marshal.PtrToStringUni(blob.data, (int)blob.size / 2).TrimEnd('\0');
        }

        private static string TryGetAppId(string path)
        {
            if (!File.Exists(path))
                return null;
            IntPtr appId;
            if (W.FwpmGetAppIdFromFileName0(path, out appId) != 0)
                return null;
            try
            {
                return ReadBlob(appId);
            }
            finally
            {
                W.FwpmFreeMemory0(ref appId);
            }
        }

        // Matches by application id as well as by path, so a short (8.3) or differently written
        // path still finds its rules, and a deleted program can still be unblocked by path.
        private List<ulong> FilterIdsFor(string path, string appId)
        {
            List<ulong> ids = new List<ulong>();
            foreach (BlockedApp app in Blocked())
            {
                bool sameId = appId != null && string.Equals(app.AppId, appId, StringComparison.OrdinalIgnoreCase);
                if (sameId || PathUtil.Same(app.Path, path))
                    ids.AddRange(app.FilterIds);
            }
            return ids;
        }

        private static FWPM_FILTER_CONDITION0 FlagsCondition(uint matchType)
        {
            FWPM_FILTER_CONDITION0 condition = new FWPM_FILTER_CONDITION0();
            condition.fieldKey = ConditionFlags;
            condition.matchType = matchType;
            condition.conditionValue.type = W.FWP_UINT32;
            condition.conditionValue.value = new IntPtr(W.FWP_CONDITION_FLAG_IS_LOOPBACK);
            return condition;
        }

        private static FWPM_FILTER_CONDITION0 PortCondition(ushort port)
        {
            FWPM_FILTER_CONDITION0 condition = new FWPM_FILTER_CONDITION0();
            condition.fieldKey = ConditionRemotePort;
            condition.matchType = W.FWP_MATCH_EQUAL;
            condition.conditionValue.type = W.FWP_UINT16;
            condition.conditionValue.value = new IntPtr(port);
            return condition;
        }

        // Provider and sublayer stay in place between rules. They are added outside the filter
        // transaction because "already exists" is the normal answer here.
        private void EnsureProviderAndSublayer()
        {
            FWPM_PROVIDER0 provider = new FWPM_PROVIDER0();
            provider.providerKey = ProviderKey;
            provider.displayData.name = "NetMute";
            provider.displayData.description = "Per-program network switch";
            provider.flags = W.FWPM_PROVIDER_FLAG_PERSISTENT;
            CheckExcept(W.FwpmProviderAdd0(engine, ref provider, IntPtr.Zero), W.FWP_E_ALREADY_EXISTS);

            FWPM_SUBLAYER0 sublayer = new FWPM_SUBLAYER0();
            sublayer.subLayerKey = SublayerKey;
            sublayer.displayData.name = "NetMute";
            sublayer.displayData.description = "Block rules for single programs";
            sublayer.flags = W.FWPM_SUBLAYER_FLAG_PERSISTENT;
            sublayer.providerKey = providerKeyPtr;
            sublayer.weight = SublayerWeight;
            CheckExcept(W.FwpmSubLayerAdd0(engine, ref sublayer, IntPtr.Zero), W.FWP_E_ALREADY_EXISTS);
        }

        private void AddFilter(Guid layer, string path, FWPM_FILTER_CONDITION0[] conditions)
        {
            int size = Marshal.SizeOf(typeof(FWPM_FILTER_CONDITION0));
            IntPtr native = Marshal.AllocHGlobal(size * conditions.Length);
            try
            {
                for (int i = 0; i < conditions.Length; i++)
                    Marshal.StructureToPtr(conditions[i], native + i * size, false);

                FWPM_FILTER0 filter = new FWPM_FILTER0();
                filter.filterKey = Guid.NewGuid();
                filter.displayData.name = "NetMute: " + Path.GetFileName(path);
                filter.displayData.description = path;
                filter.flags = W.FWPM_FILTER_FLAG_PERSISTENT;
                filter.providerKey = providerKeyPtr;
                filter.layerKey = layer;
                filter.subLayerKey = SublayerKey;
                filter.weight.type = W.FWP_EMPTY;
                filter.numFilterConditions = (uint)conditions.Length;
                filter.filterCondition = native;
                filter.action.type = W.FWP_ACTION_BLOCK;

                ulong id;
                Check(W.FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out id));
            }
            finally
            {
                Marshal.FreeHGlobal(native);
            }
        }

        private void DeleteFilter(ulong id)
        {
            CheckExcept(W.FwpmFilterDeleteById0(engine, id), W.FWP_E_FILTER_NOT_FOUND);
        }

        private void Transaction(Action work)
        {
            Check(W.FwpmTransactionBegin0(engine, 0));
            try
            {
                work();
                Check(W.FwpmTransactionCommit0(engine));
            }
            catch
            {
                W.FwpmTransactionAbort0(engine);
                throw;
            }
        }

        private static void Check(uint code)
        {
            if (code != 0)
                throw new WfpException(code);
        }

        private static void CheckExcept(uint code, uint allowed)
        {
            if (code != allowed)
                Check(code);
        }
    }
}
