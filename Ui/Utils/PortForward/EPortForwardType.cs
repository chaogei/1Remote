namespace _1RM.Utils.PortForward
{
    /// <summary>
    /// Which direction a standing forward runs in. The names mirror the OpenSSH flags so anyone who already
    /// knows <c>ssh -L</c> recognises them without reading the description.
    /// </summary>
    public enum EPortForwardType
    {
        /// <summary>
        /// <c>-L</c>: listen here, and have the SSH host connect onward to the destination for each caller.
        /// The usual shape — reach something that only the SSH host can see.
        /// </summary>
        Local = 0,

        /// <summary>
        /// <c>-R</c>: the SSH host listens, and hands each caller back to us to connect onward. Used to
        /// expose something on this machine, or reachable from it, to the far side.
        /// </summary>
        Remote = 1,

        /// <summary>
        /// <c>-D</c>: listen here as a SOCKS proxy and let the SSH host resolve and reach whatever each
        /// caller asks for. There is no fixed destination.
        /// </summary>
        Dynamic = 2,
    }

    public static class PortForwardTypeName
    {
        /// <summary>
        /// How a type is spelled in the UI. The OpenSSH flag is part of the label on purpose: it is the
        /// fastest way for someone to confirm they picked the direction they meant.
        /// </summary>
        public static string Of(EPortForwardType type) => type switch
        {
            EPortForwardType.Local => "Local (-L)",
            EPortForwardType.Remote => "Remote (-R)",
            EPortForwardType.Dynamic => "Dynamic SOCKS (-D)",
            _ => type.ToString(),
        };
    }
}
