/* ===========================================================================
   dbo.AccessRefreshTokens

   The one table the cookie-auth flow needs that JianeTechSolar does not yet
   have. It is the authority behind a signed-in session: the jt_rtk cookie
   carries a raw Guid, and this table stores only its SHA-256, so a leaked row
   cannot be replayed as a cookie value.

   Shape deliberately mirrors the existing PasswordResetTokens /
   UserActivationTokens tables (nchar(64) TokenId, the same CreatedOn/CreatedBy,
   ExpiredOn, InvalidatedOn/InvalidatedBy, UsedOn lifecycle columns) and adds
   TokenType to distinguish a rotated short-lived grant from a reused
   "remember me" one.

   Lifecycle:
     UsedOn        set when a short-lived grant is consumed by rotation.
                   A grant presented again after this is a REPLAY, and the
                   service revokes every live grant for that user.
     InvalidatedOn set by sign-out or by replay revocation.
     ExpiredOn     hard stop regardless of the two above.

   Run against: Server=EXTRASERVER;Database=JianeTechSolar
   =========================================================================== */

IF OBJECT_ID(N'[dbo].[AccessRefreshTokens]', N'U') IS NOT NULL
BEGIN
    PRINT 'dbo.AccessRefreshTokens already exists - nothing to do.';
    RETURN;
END
GO

CREATE TABLE [dbo].[AccessRefreshTokens]
(
    /* SHA-256 (hex) of the Guid handed to the browser - never the raw value. */
    [TokenId]        NCHAR(64)        NOT NULL,
    [UserId]         UNIQUEIDENTIFIER NOT NULL,

    /* JianeTech.Data.Enums.TokenTypeEnum: 0 = ShortLived, 1 = LongLived. */
    [TokenType]      INT              NOT NULL,

    [CreatedOn]      DATETIME         NOT NULL,
    [CreatedBy]      UNIQUEIDENTIFIER NULL,
    [ExpiredOn]      DATETIME         NULL,
    [InvalidatedOn]  DATETIME         NULL,
    [InvalidatedBy]  UNIQUEIDENTIFIER NULL,
    [UsedOn]         DATETIME         NULL,

    CONSTRAINT [PK_AccessRefreshTokens] PRIMARY KEY CLUSTERED ([TokenId] ASC),

    CONSTRAINT [FK_AccessRefreshTokens_UserId]
        FOREIGN KEY ([UserId])        REFERENCES [dbo].[Users] ([UserId]),

    CONSTRAINT [FK_AccessRefreshTokens_CreatedBy]
        FOREIGN KEY ([CreatedBy])     REFERENCES [dbo].[Users] ([UserId]),

    CONSTRAINT [FK_AccessRefreshTokens_InvalidatedBy]
        FOREIGN KEY ([InvalidatedBy]) REFERENCES [dbo].[Users] ([UserId])
);
GO

/* Sign-in supersedes, rotation consumes and sign-out revokes - all three scan a
   user's live grants, which is this exact predicate. */
CREATE NONCLUSTERED INDEX [IX_AccessRefreshTokens_UserId_Live]
    ON [dbo].[AccessRefreshTokens] ([UserId] ASC, [UsedOn] ASC, [InvalidatedOn] ASC)
    INCLUDE ([ExpiredOn], [TokenType]);
GO

PRINT 'dbo.AccessRefreshTokens created.';
GO
