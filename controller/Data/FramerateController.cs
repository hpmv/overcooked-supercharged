using System;
using System.Threading;

namespace Hpmv {
    public class FramerateController {
        public TimeSpan Delay { get; set; }

        public FramerateController() {
            Delay = TimeSpan.Zero;
        }

        private DateTime frameTime = DateTime.Now;

        public void WaitTillNextFrame() {
            while (DateTime.Now < frameTime) {
                Thread.Sleep(frameTime - DateTime.Now);
            }
            if (frameTime < DateTime.Now - Delay) {
                frameTime = DateTime.Now;
            }
            frameTime += Delay;
        }
    }
}