using System;
using System.Runtime.InteropServices;

namespace ArpCSharp
{
    public static class NvdaController
    {
        [DllImport("nvdaControllerClient64.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
        public static extern int nvdaController_speakText(string text);

        [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int nvdaController_cancelSpeech();

        [DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int nvdaController_testIfRunning();
        
        public static bool IsRunning()
        {
            try
            {
                return nvdaController_testIfRunning() == 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool Speak(string text)
        {
            try
            {
                if (IsRunning())
                {
                    return nvdaController_speakText(text) == 0;
                }
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
        
        public static bool CancelSpeech()
        {
            try
            {
                if (IsRunning())
                {
                    return nvdaController_cancelSpeech() == 0;
                }
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
