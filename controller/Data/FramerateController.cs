using System;
using System.Threading;

namespace Hpmv {
    public class FramerateController {
        public int Framerate { get; set; } = 60;

        private DateTime frameTime = DateTime.Now;

        public void WaitTillNextFrame() {
            while (DateTime.Now < frameTime) {
                Thread.Sleep(frameTime - DateTime.Now);
            }
            var delay =  TimeSpan.FromSeconds(1) / Framerate;
            if (frameTime < DateTime.Now - delay) {
                frameTime = DateTime.Now;
            }
            frameTime += delay;
        }
    }
}