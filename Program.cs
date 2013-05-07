using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HowMuchDoIType
{
    static class Program
    {

        #region DllImport

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);        

        #endregion

        public static List<int> keyCodes = new List<int>()
        { 
            0x08, // Backspace
            0x20, // Space
            0X30, 0X31, 0X32, 0X33, 0X34, 0X35, 0X36, 0X37, 0X38, 0X39, // 0...9
            0X41, 0X42, 0X43, 0X44, 0X45, 0X46, 0X47, 0X48, 0X49, 0X4A, 0X4B, 0X4C, 0X4D, 0X4E, 0X4F, 0X50, 0X51, 0X52, 0X53, 0X54, 0X55, 0X56, 0X57, 0X58, 0X59, 0X5A, // A...Z
            0X60, 0X61, 0X62, 0X63, 0X64, 0X65, 0X66, 0X67, 0X68, 0X69, // Numpad 0...9
            0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F, // Special on Numpad
            0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE2 // А это русиш
        };

        public static Dictionary<int, int> keyCountTotal = new Dictionary<int, int>();
        public static Dictionary<int, int> keyCountNetto = new Dictionary<int, int>();

        private static FormMain formMain;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        private static string appData = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\HowMuchDoIType";

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static Thread threadWrite;
        private static bool abortWrite = false;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            formMain = new FormMain();
            formMain.notifyIconMain.Visible = true;
            formMain.menuItemExit.Click += menuItemExit_Click;

            PrepareAppData();

            threadWrite = new Thread(new ThreadStart(ThreadWrite));
            threadWrite.Priority = ThreadPriority.Normal;
            threadWrite.IsBackground = true;
            threadWrite.Start();

            _hookID = SetHook(_proc);

            Application.Run();
        }

        private static void PrepareAppData()
        {
            // Создаем папку, если ее нет
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);

            // Смотрим наличие файла с сегодняшней датой
            DateTime dtNow = DateTime.Now;
            string currentDateFile = dtNow.Year.ToString() + "-" + dtNow.Month.ToString().PadLeft(2, '0') + "-" + dtNow.Day.ToString().PadLeft(2, '0') + ".txt";
            string todayFile = appData + "\\" + currentDateFile;
            string fileContent = string.Empty;

            keyCountTotal.Add(DateToInt(dtNow), 0);
            keyCountNetto.Add(DateToInt(dtNow), 0);

            if (File.Exists(todayFile))
            { 
                // Файл существует, считываем его
                try
                {
                    using (StreamReader sr = new StreamReader(todayFile))
                    {
                        fileContent = sr.ReadToEnd();
                    }
                }
                catch (Exception e) { }
            }

            if (fileContent.Length != 0)
            { 
                // Что-то считалось, по идее, это должно быть 20000\r\n10000
                string[] splitArray = fileContent.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                if (splitArray.Length == 2)
                {
                    int result1 = 0;
                    int result2 = 0;

                    if (int.TryParse(splitArray[0], out result1)) keyCountTotal[DateToInt(dtNow)] = result1;
                    if (int.TryParse(splitArray[1], out result2)) keyCountNetto[DateToInt(dtNow)] = result2;
                }
            }

            UpdateText(keyCountTotal[DateToInt(dtNow)], keyCountNetto[DateToInt(dtNow)]);
        }

        private static int DateToInt(DateTime dt)
        {
            return dt.Year * 365 + dt.Month * 31 + dt.Day;
        }

        private static void UpdateText(int total, int netto)
        {
            formMain.notifyIconMain.Text = "Набрано всего: " + total.ToString() + "\r\nНабрано нетто: " + netto.ToString();
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }
        
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (keyCodes.Contains(vkCode))
                {
                    int dayNumber = DateToInt(DateTime.Now);
                    if (!keyCountTotal.ContainsKey(dayNumber)) keyCountTotal.Add(dayNumber, 0);
                    if (!keyCountNetto.ContainsKey(dayNumber)) keyCountNetto.Add(dayNumber, 0);

                    if (vkCode == 0x08)
                    {
                        // Пользователь нажал Backspace

                        // Количество Total не меняется
                        // Количество Netto уменьшается на 1 символ
                        keyCountNetto[dayNumber]--;
                    }
                    else
                    { 
                        // Пользователь нажал символ

                        // Количество Total и Netto увеличивается на 1 символ
                        keyCountTotal[dayNumber]++;
                        keyCountNetto[dayNumber]++;
                    }

                    // Обновляем toolTip для notifyIcon
                    UpdateText(keyCountTotal[dayNumber], keyCountNetto[dayNumber]);
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        
        static void menuItemExit_Click(object sender, EventArgs e)
        {
            UnhookWindowsHookEx(_hookID);

            formMain.notifyIconMain.Visible = false;
            abortWrite = true;

            Application.Exit();
        }

        static void ThreadWrite()
        {
            int cycleCount = 0;

            do
            {
                if (abortWrite) break;

                if (cycleCount == 60)
                {
                    DateTime dtNow = DateTime.Now;
                    int dayNumber = DateToInt(dtNow);

                    int total = 0;
                    int netto = 0;

                    if (keyCountTotal.ContainsKey(dayNumber)) total = keyCountTotal[dayNumber];
                    if (keyCountTotal.ContainsKey(dayNumber)) netto = keyCountNetto[dayNumber];

                    // Вот тут физическая запись
                    string toWrite = total.ToString() + "\r\n" + netto.ToString();
                    string currentDateFile = dtNow.Year.ToString() + "-" + dtNow.Month.ToString().PadLeft(2, '0') + "-" + dtNow.Day.ToString().PadLeft(2, '0') + ".txt";
                    string todayFile = appData + "\\" + currentDateFile;

                    File.WriteAllLines(todayFile, new string[] { toWrite });

                    cycleCount = 0;
                }

                cycleCount++;
                Thread.Sleep(1000);
            }
            while (true);
        }
    }
}
