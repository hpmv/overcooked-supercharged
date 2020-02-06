using System.Threading;

namespace Hpmv {
    public class Injector {
        public static InjectorServer Server { get; private set; }

        static Injector() {
            Server = new InjectorServer();
            Server.Start();
        }
    }
}