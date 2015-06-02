﻿using ExternalUtilsCSharp;
using ExternalUtilsCSharp.MathObjects;
using ExternalUtilsCSharp.MemObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CSGOTriggerbot
{
    class Program
    {
        #region OFFSETS
        public static int offsetEntityList = 0x00;
        private static int offsetLocalPlayer = 0x00;
        private static int offsetJump = 0x00;
        private static int offsetClientState = 0x00;
        private static int offsetSetViewAngles = 0x00;
        private static int offsetGlowManager = 0x00;
        private static int offsetSignOnState = 0xE8;
        public static int offsetWeaponH = 0x12C0;   // m_hActiveWeapon
        public static int offsetWeaponId = 0x1690;   // Search for weaponid
        #endregion

        #region VARIABLES
        private static bool m_bWork;
        private static Vector3 vecPunch = Vector3.Zero;
        private static KeyUtils keyUtils;
        private static CSGOConfigUtils configUtils;
        private static MemUtils memUtils;
        #endregion

        static void Main(string[] args)
        {
            m_bWork = true;

            keyUtils = new KeyUtils();
            configUtils = new CSGOConfigUtils();
            configUtils.SetValue("rcsFullCompensation", false);
            configUtils.SetValue("rcsEnabled", true);
            configUtils.SetValue("bunnyhopEnabled", true);
            configUtils.SetValue("triggerbotKey", WinAPI.VirtualKeyShort.XBUTTON1);
            configUtils.SetValue("aimlockEnabled", true);
            configUtils.SetValue("glowEnabled", true);
            configUtils.ReadSettingsFromFile("config.cfg");
            memUtils = new MemUtils();
            memUtils.UseUnsafeReadWrite = true;

            Thread thread = new Thread(new ThreadStart(Loop));
            thread.IsBackground = true;
            thread.Start();

            Console.WriteLine("Press ESC to exit");
            Console.WriteLine("Press NUMPAD0 to toggle RCS on/off");
            Console.WriteLine("Press NUMPAD1 to toggle RCS mode");
            Console.WriteLine("Press NUMPAD2 to toggle bunnyhop on/off");
            Console.WriteLine("Press NUMPAD3 to toggle aimlock");
            Console.WriteLine("Hold SPACE for bunnyhop");
            Console.WriteLine("Hold {0} for triggerbot", configUtils.GetValue<WinAPI.VirtualKeyShort>("triggerbotKey"));

            while (!m_bWork) Thread.Sleep(250);

            Console.WriteLine("Waiting for thread to exit...");
            thread.Join();

            configUtils.SaveSettingsToFile("config.cfg");
            Console.WriteLine("Bye.");
        }

        private static void Loop()
        {
            ProcUtils proc;
            ProcessModule clientDll = null;
            ProcessModule engineDll = null;
            byte[] data;
            GlowObjectDefinition[] glowObjects = new GlowObjectDefinition[128];
            CSGOPlayer[] players = new CSGOPlayer[8192];
            int[] playerAddresses = new int[players.Length];
            int entityListAddress;
            int localPlayerAddress;
            int clientStateAddress;
            int glowAddress;
            int glowCount;
            int setViewAnglesAddress;
            SignOnState signOnState;
            CSGOPlayer nullPlayer = new CSGOPlayer() { m_iID = 0, m_iHealth = 0, m_iTeam = 0 };
            CSGOLocalPlayer localPlayer;
            int lastAimlockTargetIdx = 0;
            Stopwatch lastSeen = new Stopwatch();
            Bones[] aimlockBones = new Bones[] { Bones.Head, Bones.Neck, Bones.Spine1, Bones.Spine2, Bones.Spine3, Bones.Spine4, Bones.Spine5 };

            //Wait for process to spawn
            while (!ProcUtils.ProcessIsRunning("csgo") && m_bWork) { Thread.Sleep(500); }
            if (!m_bWork)
                return;

            proc = new ProcUtils("csgo", WinAPI.ProcessAccessFlags.VirtualMemoryRead | WinAPI.ProcessAccessFlags.VirtualMemoryWrite | WinAPI.ProcessAccessFlags.VirtualMemoryOperation);
            memUtils.Handle = proc.Handle;

            //Get client.dll & engine.dll
            while (clientDll == null) { clientDll = proc.GetModuleByName(@"bin\client.dll"); }
            while (engineDll == null) { engineDll = proc.GetModuleByName("engine.dll"); }

            int clientDllBase = clientDll.BaseAddress.ToInt32();
            int engineDllBase = engineDll.BaseAddress.ToInt32();

            #region OFFSETS
            ScanResult scan;
            //EntityList
            scan = memUtils.PerformSignatureScan(new byte[] { 0x05, 0x00, 0x00, 0x00, 0x00, 0xC1, 0xe9, 0x00, 0x39, 0x48, 0x04 }, "x????xx?xxx", clientDll);
            if (scan.Success)
            {
                int tmp = memUtils.Read<int>((IntPtr)(scan.Address.ToInt32() + 1));
                byte tmp2 = memUtils.Read<byte>((IntPtr)(scan.Address.ToInt32() + 7));
                offsetEntityList = tmp + tmp2 - clientDllBase;
            }
            //LocalPlayer
            scan = memUtils.PerformSignatureScan(new byte[] { 0x8D, 0x34, 0x85, 0x00, 0x00, 0x00, 0x00, 0x89, 0x15, 0x00, 0x00, 0x00, 0x00, 0x8B, 0x41, 0x08, 0x8B, 0x48 }, "xxx????xx????xxxxx", clientDll);
            if (scan.Success)
            {
                int tmp = memUtils.Read<int>((IntPtr)(scan.Address.ToInt32() + 3));
                byte tmp2 = memUtils.Read<byte>((IntPtr)(scan.Address.ToInt32() + 18));
                offsetLocalPlayer = tmp + tmp2 - clientDllBase;
            }
            //+jump
            scan = memUtils.PerformSignatureScan(new byte[] { 0x89, 0x15, 0x00, 0x00, 0x00, 0x00, 0x8B, 0x15, 0x00, 0x00, 0x00, 0x00, 0xF6, 0xC2, 0x03, 0x74, 0x03, 0x83, 0xCE, 0x08, 0xA8, 0x08, 0xBF }, "xx????xx????xxxxxxxxxxx", clientDll);
            if (scan.Success)
            {
                int tmp = memUtils.Read<int>((IntPtr)(scan.Address.ToInt32() + 2));
                offsetJump = tmp - clientDllBase;
            }
            //ClientState
            scan = memUtils.PerformSignatureScan(new byte[] { 0xC2, 0x00, 0x00, 0xCC, 0xCC, 0x8B, 0x0D, 0x00, 0x00, 0x00, 0x00, 0x33, 0xC0, 0x83, 0xB9 }, "x??xxxx????xxxx", engineDll);
            if (scan.Success)
            {
                int tmp = memUtils.Read<int>((IntPtr)(scan.Address.ToInt32() + 7));
                offsetClientState = tmp - engineDllBase;
            }
            //SetViewAngles
            scan = memUtils.PerformSignatureScan(new byte[] { 0x8B, 0x15, 0x00, 0x00, 0x00, 0x00, 0x8B, 0x4D, 0x08, 0x8B, 0x82, 0x00, 0x00, 0x00, 0x00, 0x89, 0x01, 0x8B, 0x82, 0x00, 0x00, 0x00, 0x00, 0x89, 0x41, 0x04 }, "xx????xxxxx????xxxx????xxx", engineDll);
            if (scan.Success)
            {
                int tmp = memUtils.Read<int>((IntPtr)(scan.Address.ToInt32() + 11));
                offsetSetViewAngles = tmp;
            }
            //GlowObject
            scan = memUtils.PerformSignatureScan(new byte[] { 0x8D, 0x8F, 0x00, 0x00, 0x00, 0x00, 0xA1, 0x00, 0x00, 0x00, 0x00, 0xC7, 0x04, 0x02, 0x00, 0x00, 0x00, 0x00, 0x89, 0x35, 0x00, 0x00, 0x00, 0x00, 0x8B, 0x51 }, "xx????x????xxx????xx????xx", clientDll);
            if (scan.Success)
            {
                int tmp = memUtils.Read<int>((IntPtr)(scan.Address.ToInt32() + 7));
                offsetGlowManager = tmp - clientDllBase;
            }
            #endregion

            //Run hack
            while (proc.IsRunning && m_bWork)
            {
                Thread.Sleep((int)(1000f / 120f));

                //Don't do anything if game is not in foreground
                if (WinAPI.GetForegroundWindow() != proc.Process.MainWindowHandle)
                    continue;

                #region Handling input
                keyUtils.Update();
                if (keyUtils.KeyWentUp(WinAPI.VirtualKeyShort.ESCAPE))
                    m_bWork = false;
                if (keyUtils.KeyWentUp(WinAPI.VirtualKeyShort.NUMPAD0))
                    configUtils.SetValue("rcsEnabled", !configUtils.GetValue<bool>("rcsEnabled"));
                if (keyUtils.KeyWentUp(WinAPI.VirtualKeyShort.NUMPAD1))
                    configUtils.SetValue("rcsFullCompensation", !configUtils.GetValue<bool>("rcsFullCompensation"));
                if (keyUtils.KeyWentUp(WinAPI.VirtualKeyShort.NUMPAD2))
                    configUtils.SetValue("bunnyhopEnabled", !configUtils.GetValue<bool>("bunnyhopEnabled"));
                if (keyUtils.KeyWentUp(WinAPI.VirtualKeyShort.NUMPAD3))
                    configUtils.SetValue("aimlockEnabled", !configUtils.GetValue<bool>("aimlockEnabled"));
                #endregion

                #region Various addresses
                entityListAddress = clientDll.BaseAddress.ToInt32() + offsetEntityList;
                localPlayerAddress = memUtils.Read<int>((IntPtr)(offsetLocalPlayer + clientDllBase));
                localPlayer = memUtils.Read<CSGOLocalPlayer>((IntPtr)(localPlayerAddress));
                clientStateAddress = memUtils.Read<int>((IntPtr)(engineDllBase + offsetClientState));
                setViewAnglesAddress = clientStateAddress + offsetSetViewAngles;
                #endregion

                signOnState = (SignOnState)memUtils.Read<int>((IntPtr)(clientStateAddress + offsetSignOnState));
                //Sanity checks
                if (signOnState != SignOnState.SIGNONSTATE_FULL || !localPlayer.IsValid())
                    continue;

                #region Reading entitylist and entities
                memUtils.Read((IntPtr)(clientDllBase + offsetEntityList), out data, 16 * players.Length);

                //Read entities (players)
                for (int i = 0; i < data.Length / players.Length; i++)
                {
                    int address = BitConverter.ToInt32(data, i * 16);
                    playerAddresses[i] = address;
                    if (address != 0)
                    {
                        players[i] = memUtils.Read<CSGOPlayer>((IntPtr)(address), nullPlayer);
                        if (players[i].GetClassID(memUtils) != 34)
                            players[i] = nullPlayer;
                    }
                    else
                    {
                        players[i] = nullPlayer;
                    }
                }
                #endregion
                #region Triggerbot
                if (keyUtils.KeyIsDown(configUtils.GetValue<WinAPI.VirtualKeyShort>("triggerbotKey")))
                {
                    if (localPlayer.m_iCrosshairIdx > 0 && localPlayer.m_iCrosshairIdx <= players.Length)
                    {
                        CSGOPlayer target = players[localPlayer.m_iCrosshairIdx - 1];
                        if (target.IsValid(memUtils))
                        {
                            if (target.m_iTeam != localPlayer.m_iTeam)
                            {
                                Console.WriteLine("*Shoot*");
                                Thread.Sleep(10);
                                WinAPI.mouse_event(WinAPI.MOUSEEVENTF.LEFTDOWN, 0, 0, 0, 0);
                                Thread.Sleep(10);
                                WinAPI.mouse_event(WinAPI.MOUSEEVENTF.LEFTUP, 0, 0, 0, 0);
                                Thread.Sleep(10);
                            }
                        }
                    }
                }
                #endregion
                #region Aimlock
                if (configUtils.GetValue<bool>("aimlockEnabled"))
                {
                    CSGOPlayer target = nullPlayer;
                    //Is there a player in our crosshair?
                    if (localPlayer.m_iCrosshairIdx > 0 && localPlayer.m_iCrosshairIdx <= players.Length)
                    {
                        lastAimlockTargetIdx = localPlayer.m_iCrosshairIdx;
                        lastSeen.Reset();
                        lastSeen.Start();
                        target = players[lastAimlockTargetIdx - 1];
                    }
                    //Or is the last player we aimed at valid? (nonzero index)
                    else if (lastAimlockTargetIdx != 0)
                    {
                        //Did we see him less than a half second ago?
                        if (lastSeen.ElapsedMilliseconds < 500)
                        {
                            target = players[lastAimlockTargetIdx - 1];
                        }
                        else
                        {
                            lastAimlockTargetIdx = 0;
                        }
                    }
                    if (target.IsValid(memUtils))
                    {
                        if (target.m_iTeam != localPlayer.m_iTeam)
                        {
                            Vector3 sourceVector = localPlayer.m_vecOrigin + localPlayer.m_vecViewOffset;
                            Vector3 originalAimAngles = memUtils.Read<Vector3>((IntPtr)setViewAnglesAddress);
                            Vector3 smallestAimAngles = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                            foreach (Bones bone in aimlockBones)
                            {
                                Vector3 targetVector = new Vector3(memUtils.Read<float>((IntPtr)(target.GetBoneAddress((int)bone)), new int[] { 0x0c, 0x1C, 0x2C }));
                                Vector3 aimAngles = sourceVector.CalcAngle(targetVector);
                                aimAngles = aimAngles.ClampAngle();
                                if ((aimAngles - originalAimAngles).Length() < smallestAimAngles.Length())
                                {
                                    smallestAimAngles = aimAngles - originalAimAngles;
                                }
                            }
                            memUtils.Write<Vector3>((IntPtr)(setViewAnglesAddress), originalAimAngles + smallestAimAngles);
                        }
                    }
                }
                #endregion
                #region RCS
                if (configUtils.GetValue<bool>("rcsEnabled"))
                {
                    if (localPlayer.m_iShotsFired > 0)
                    {
                        Vector3 currentPunch = localPlayer.m_vecPunch - vecPunch;
                        Vector3 viewAngles = memUtils.Read<Vector3>((IntPtr)(setViewAnglesAddress), Vector3.Zero);
                        Vector3 newViewAngles = viewAngles - (configUtils.GetValue<bool>("rcsFullCompensation") ? currentPunch * 2f : currentPunch);
                        newViewAngles = newViewAngles.ClampAngle();
                        memUtils.Write<Vector3>((IntPtr)(setViewAnglesAddress), newViewAngles);
                    }
                    vecPunch = localPlayer.m_vecPunch;
                }
                #endregion
                #region Bunnyhop
                if (configUtils.GetValue<bool>("bunnyhopEnabled"))
                {
                    if (keyUtils.KeyIsDown(WinAPI.VirtualKeyShort.SPACE))
                    {
                        if ((localPlayer.m_iFlags & 1) == 1) //Stands (FL_ONGROUND)
                            memUtils.Write<int>((IntPtr)(clientDllBase + offsetJump), 5);
                        else
                            memUtils.Write<int>((IntPtr)(clientDllBase + offsetJump), 4);
                    }
                }
                #endregion
                #region Glow
                if (configUtils.GetValue<bool>("glowEnabled"))
                {
                    glowAddress = memUtils.Read<int>((IntPtr)(clientDllBase + offsetGlowManager));
                    glowCount = memUtils.Read<int>((IntPtr)(clientDllBase + offsetGlowManager + 0x04));
                    int size = Marshal.SizeOf(typeof(GlowObjectDefinition));
                    memUtils.Read((IntPtr)(glowAddress), out data, size * glowCount);
                        for (int i = 0; i < glowCount && i < glowObjects.Length; i++)
                        {
                            glowObjects[i] = data.GetStructure<GlowObjectDefinition>(i * size, size);
                            for (int idx = 0; idx < players.Length; idx++)
                            {
                                if (glowObjects[i].pEntity != 0 && playerAddresses[idx] == glowObjects[i].pEntity)
                                {
                                    if (!players[idx].IsValid(memUtils))
                                        break;
                                    glowObjects[i].a = 1f;
                                    glowObjects[i].r = (players[idx].m_iTeam == 2 ? 1f : 0f);
                                    glowObjects[i].g = 0;
                                    glowObjects[i].b = (players[idx].m_iTeam == 3 ? 1f : 0f);
                                    glowObjects[i].m_bRenderWhenOccluded = true;
                                    glowObjects[i].m_bRenderWhenUnoccluded = true;
                                    glowObjects[i].m_bFullBloom = false;
                                    memUtils.Write<GlowObjectDefinition>((IntPtr)(glowAddress + size * i), glowObjects[i], 4, size - 14);
                                    break;
                                }
                            }
                        }
                    }
                #endregion
            }
        }
    }
}
