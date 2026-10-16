# ADR-005: Mandant self-registration and login identity

- Status: Accepted
- Date: 2026-10-16

## Context

A visitor without a session must be able to request a new Mandant (Gesellschaft) from the login page. The first user of that Mandant is always `kunde`. A later rights-management step will add more users. One email address may belong to users in several Mandanten, and to more than one user inside a single Mandant — for example a normal accountant and an administrator. Those are separate logins. Roles are not part of this decision.

The Cognito user pool already treats email as a unique sign-in alias. ADR-002 still requires one database per SaaS customer. This development cell has one database and separates Gesellschaften by `tenant_id`. Registration in this slice creates a Gesellschaft in the current cell. It does not provision a new customer database.

## Decision

- Public registration collects **Firmenname** (required) and the mailbox that will receive the password. It stores a pending Mandant and the user `kunde`. It does not create a Cognito user yet.
- After creation, the API mails `ckrause@cvs.de` (`Registration:ApproverEmail`) a single-use approval link. The password is generated and mailed to the registrant only after that link is used. Until then the Mandant cannot be used to sign in.
- Every login asks for **Mandantennummer**, **User**, and **Passwort**. The form is not prefilled from an earlier session.
- The login key is `(Mandantennummer, User)`. Email is contact data on the user row, is not unique, and is not written onto the Cognito user. The Cognito username is `{mandantennummer}.{user}` (for example `1001.kunde`). Each such login is its own Cognito account.
- Mandantennummern come from a sequence starting at `1001`. After login the status bar shows `Mandant {number} · {Firmenname}`.
- The approval token is stored only as a SHA-256 hash, expires after 7 days, and does nothing once the Mandant is active. If the customer mail fails, the Mandant stays pending; a later click sets a new password and sends the mail again.

## Alternatives

- Send the password at registration and use the approval link only to unlock login. Rejected: the secret would exist before permission was given.
- Login with Mandantennummer and password only, always implying user `kunde`. Rejected: later users in the same Mandant need an explicit user field.
- Use the registrant email as the Cognito username. Rejected: the pool requires unique emails, and one mailbox must be able to back several users, including two in one Mandant.

## Consequences

- The API needs anonymous registration and approval routes, Cognito admin rights to create the user, and Amazon SES in production. Development may log the mail instead of sending it, and must not log the password in production.
- Rights such as Buchhalter and Administrator are a follow-up. That work must keep two users in one Mandant as two logins, not two roles on one Cognito account.
- Database-per-customer provisioning stays with ADR-002 and is not performed by this flow.
