namespace Mako.Web;

/// The sample script shown in the web REPL on first load — a plain .cs
/// file (not embedded in the .razor markup) so the raw MAKO source isn't
/// subject to Razor's own @-parsing rules.
static class DefaultScript
{
    public const string Text = """
        using Physics3D;

        fn square(n) {
            return n * n;
        }

        main() {
            print "MAKO running in the browser.";
            print "3 squared is " + square(3);

            world = Physics3D.world(0, -9.81, 0);
            floor = Physics3D.plane(world, 0, 1, 0, 0);
            ball = Physics3D.sphere(world, "dynamic", 0, 5, 0, 1, 1);
            for i in range(120) {
                Physics3D.step(world, 1 / 60);
            }
            info = Physics3D.body_info(world, ball);
            print "ball settled at y=" + round(info["y"] * 100) / 100;
        }
        """;
}
