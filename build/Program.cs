using Build;
using Cake.Frosting;

return new CakeHost()
    .UseContext<BuildContext>()
    .UseWorkingDirectory("..")
    .Run(args);
