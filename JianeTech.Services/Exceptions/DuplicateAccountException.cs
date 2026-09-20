namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// Raised when a public registration names an email address that already has an
    /// account. Derives from the service-scoped exception so a controller that only
    /// catches the base type still returns a 400, while one that catches this first can
    /// answer without confirming that the address is on file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The distinction exists for one endpoint and one reason. <c>UsersApiController</c>
    /// is behind <c>[Authorize]</c>, so telling an administrator "that address is already
    /// registered" discloses nothing they cannot read off the register in front of them.
    /// <c>POST /api/account/register</c> is anonymous, and there the same sentence is a
    /// way of testing which addresses belong to the fund's investors and customers, one
    /// request at a time.
    /// </para>
    /// <para>
    /// So the anonymous endpoint catches this type and answers 200 with the sentence it
    /// gives every other caller — the same posture <c>RequestPasswordReset</c> takes, for
    /// the same reason. <see cref="Message"/> is what actually happened, for the log; it
    /// is not written for a reader and must not be returned to one.
    /// </para>
    /// <para>
    /// A taken <em>username</em> is deliberately not this. It has to be reported plainly
    /// or the person cannot finish the form, and unlike an email address a username is
    /// something they just invented rather than an identifier somebody else could be
    /// probing for.
    /// </para>
    /// </remarks>
    public class DuplicateAccountException : UserValidationException
    {
        public DuplicateAccountException(string message) : base(message) { }
    }
}
