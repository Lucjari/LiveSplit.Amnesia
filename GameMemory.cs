using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LiveSplit.ComponentUtil;

namespace LiveSplit.Amnesia
{
    class GameMemory
    {
        public event EventHandler<LoadingChangedEventArgs> OnLoadingChanged;

        // Amnesia.exe + UNUSED_BYTE_OFFSET is the location where we put our isLoading var
        // To find a new location for the isloading var, look for a place in memory with a lot of CC bytes and use the address
        // of the start of those CC bytes
        private const int UNUSED_BYTE_OFFSET = 0xC7BE2;

        private Process _process;
        private MemoryWatcher<bool> _isLoading;

        public void Update()
        {
            if (_process == null || _process.HasExited)
            {
                _process = null;
                if (!this.TryGetGameProcess())
                    return;
            }

            if (_isLoading.Update(_process))
                this.OnLoadingChanged?.Invoke(this, new LoadingChangedEventArgs(_isLoading.Current));
        }

        bool TryGetGameProcess()
        {
            Process p = Process.GetProcesses().FirstOrDefault(x => x.ProcessName.ToLower() == "amnesia");
            if (p == null || p.HasExited)
                return false;

            byte[] addrBytes = BitConverter.GetBytes((uint)p.MainModuleWow64Safe().BaseAddress + UNUSED_BYTE_OFFSET);

            // the following code has a very small chance to crash the game due to not suspending threads while writing memory
            // commented out stuff is for the cracked version of the game (easier to debug when there's no copy protection)

            // overwrite unused alignment byte with and initialize as our "is loading" var
            if (!p.WriteBytes(p.MainModuleWow64Safe().BaseAddress + UNUSED_BYTE_OFFSET, 0))
                return false;

            // the following patches are in Amnesia.cLuxMapHandler::CheckMapChange(afTimeStep)
            // (the game kindly provides us with a .pdb)

            // this payload is responsible for setting the loadingvar to 1
            // We overwrite useless code that is used for debug/error logging
            // Search for following bytes in memory: 83 7D E8 10 C6 45 FC 00 72 0C 8B 55 D4
            // Use the address where the 83 byte is located
            var payload1 = new List<byte>(new byte[] { 0xC6, 0x05 });
            payload1.AddRange(addrBytes);
            payload1.AddRange(new byte[] { 0x01, 0x90, 0xEB });
            if (!p.WriteBytes(p.MainModuleWow64Safe().BaseAddress + 0xC7884, payload1.ToArray()))
                return false;

            // this payload is responsible for setting the loadingvar to 0
            // We overwrite useless code that is used for debug/error logging
            // Search for following bytes in memory: FF 50 6A 02 C6 45 FC 04 E8 6A DC
            // Use the address where the 45 byte is located
            var payload2 = new List<byte>(new byte[] { 0x05 });
            payload2.AddRange(addrBytes);
            payload2.AddRange(new byte[] { 0x00, 0x90, 0x90 });
            if (!p.WriteBytes(p.MainModuleWow64Safe().BaseAddress + 0xC7A6E, payload2.ToArray()))
                return false;

            _isLoading = new MemoryWatcher<bool>(p.MainModuleWow64Safe().BaseAddress + UNUSED_BYTE_OFFSET);
            _process = p;

            return true;
        }
    }

    class LoadingChangedEventArgs : EventArgs
    {
        public bool IsLoading { get; private set; }

        public LoadingChangedEventArgs(bool isLoading)
        {
            this.IsLoading = isLoading;
        }
    }
}
