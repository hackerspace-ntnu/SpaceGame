namespace SpaceGame.Core
{
    /// <summary>
    /// Reads <c>-flag</c> and <c>-flag value</c> pairs out of a process's command line.
    ///
    /// Takes the argument array rather than reading <c>Environment.GetCommandLineArgs()</c> itself,
    /// so the callers that decide something from it — which UGS profile to sign in under, which
    /// half of the two-process autotest to run — stay testable with a made-up command line.
    /// </summary>
    public static class CommandLineArgs
    {
        public static bool Has(string[] args, string name)
        {
            if (args == null) return false;

            foreach (string arg in args)
                if (arg == name) return true;

            return false;
        }

        /// <summary>The value after <paramref name="name"/>, or null — including when it is last.</summary>
        public static string Value(string[] args, string name)
        {
            if (args == null) return null;

            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];

            return null;
        }
    }
}
