namespace NetMute
{
    // What "block this program" means to a user, shared by the window and the command line.
    internal static class Muter
    {
        public static void Block(Firewall firewall, string path)
        {
            firewall.Block(path);
            Store.Add(path);
            NetStat.CloseConnections(ProcessInfo.PidsOf(path));
        }

        public static bool Unblock(Firewall firewall, string path)
        {
            return firewall.Unblock(path);
        }

        public static void Forget(Firewall firewall, string path)
        {
            firewall.Unblock(path);
            Store.Remove(path);
        }

        public static void RemoveEverything(Firewall firewall)
        {
            firewall.RemoveEverything();
            Store.Delete();
        }
    }
}
