namespace Pathfinder.Server.Actors.Feed
{
    public enum GoodbyeReason
    {
        /// <summary>
        /// Can be sent by <see cref="Feed{TPayload}"/>s.
        /// </summary>
        Eof,

        /// <summary>
        /// Can be sent by <see cref="Feed{TPayload}"/>s and <see cref="Consumer{TPayload}"/>s.
        /// </summary>
        Cancel,

        /// <summary>
        /// Can be sent by <see cref="Feed{TPayload}"/>s and <see cref="Consumer{TPayload}"/>s.
        /// </summary>
        Error
    }
}