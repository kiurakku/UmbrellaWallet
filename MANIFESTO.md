# The rules

Umbrella is a self-custody wallet for people who may one day have to explain it to somebody they did
not choose to explain it to.

That sentence decides most of what follows. A wallet for a hobbyist can afford to be optimistic. A
wallet for somebody under pressure cannot, because the moment it matters is the moment its claims get
tested by an adversary rather than by a user.

These are the rules the code is actually held to. Each one is here because it was broken at least
once and cost something.

---

## 1. Say what is true, including the parts that sell badly

Every wallet says "your keys never leave your device". It is true and it is not the whole story: a
wallet still has to **ask somebody** what is on the chain, and on a public chain asking means handing
over the address. Tor hides your IP. It does not un-send an address.

So the wallet states what each server learns, names who runs it, and lets you point it somewhere else.
The Settings screen carries the list. The build fails if a server appears in the code without
appearing on that list, because a transparency page that can quietly fall behind the code is worse
than none — it reassures without being true, and the person reading it is reading it *because* they
need the truth.

**The test of this rule:** when a feature's honest description makes it sound weaker, the honest
description ships.

## 2. Show the limits next to the promise

"Private send" turns on Tor, waits for it, arms the kill-switch, narrows the inputs and sends change
to a fresh address. It also cannot make a public ledger private, cannot unlink addresses a spend has
already joined, and cannot hide you from the person you are paying.

Both halves render together, always. On Monero the list of things to switch on is empty and the list
of limits is not — because **"nothing to turn on" must never read as "nothing to know"**.

A reassurance that arrives without its limits is how somebody ends up trusting more than they should,
and they will not find out until it costs them.

## 3. Never substitute silently

If the user picks a Monero node, a Bitcoin explorer, a coin to spend from — that choice is honoured or
the operation fails with a reason. It is never quietly replaced with the default.

Falling back is a kindness when the *wallet* chose the server. It is a betrayal when the *user* chose
it, because the whole point of choosing was to keep those addresses away from somebody in particular.

The same rule covers a `.onion` node with Tor off: not attempted, not swapped for a clearnet one, and
the screen says which.

## 4. Fail closed, and make "we could not ask" different from "there is nothing there"

An unreachable explorer throws. It never returns an empty result, because a zero balance and an
unanswered question are not the same fact, and confusing them is how a wallet tells somebody their
money is gone.

The Tor-only kill-switch refuses connections rather than falling back to clearnet. A cached balance is
never lowered by a partial scan. A failed send preparation says so **on the Send screen**, not in a
status line somewhere else — that bug made pressing "Review" look like it did nothing at all.

## 5. Refuse rather than guess, when guessing moves money

An amount finer than a token's decimals is refused, not rounded: rounding down loses the remainder
silently and rounding up spends more than the user agreed to.

A mistyped node address is refused, not corrected — a wrong hostname is a stranger answering for the
chain. A jetton with no metadata is skipped rather than shown with assumed decimals, because a number
nobody can check against anything is worse than a missing row.

And a plain `http://` endpoint is refused outright: somebody deliberately choosing their own server
*for privacy* and then sending every address they own in clear would have made things worse while
believing they had made them better — and is the least likely person to notice.

## 6. Never issue what you cannot then find and spend

A fresh receive address per payment is the single best privacy habit on a transparent chain. It is
also how funds get stranded: if the balance scan only ever looks at the first address, money sent to
the fourth is money the user watches arrive on an explorer and can never move.

So the fresh-address button is offered exactly where the wallet scans every address it has issued
**and** can sign across them. The two lists are pinned to each other in the test suite rather than
kept in step by memory.

## 7. A chain's constants are that chain's, not Bitcoin's

Bitcoin's 546-satoshi dust limit is not universal. Applied to Dogecoin it was wrong by more than two
orders of magnitude, so a send could plan, sign and broadcast and then be refused — sometimes because
the *change* output was dust, which the user never chose and could not see.

Fee bands, dust limits, address formats, decimals: each is per-chain and cited, not inherited by
assumption.

## 8. The money path is not where convenience goes

Code that signs a spend gets new branches only when they change what is signed. A clearer error
message is not a reason to add one — that belongs in the layer the user is reading.

Amounts typed by a person go through one parser, because `decimal.TryParse` with group separators
reads "0,5" as **five**.

## 9. A test that cannot fail is worse than no test

It is counted as coverage.

Every guard in this repo is proved by breaking the thing it guards and watching it fail. One written
during this work read its input off an uninitialised object, got an empty list, and passed against the
exact bug it was written for. The probe is the point, not the assertion.

The suite does not touch the internet either. It used to — quietly, on every wallet it created — and
only looked fast because those calls were failing. A suite that depends on somebody else's uptime
gives a different answer on different days, and puts load on free public services every time anybody
runs it.

## 10. Delete means delete

"Delete everything" has to remove everything the wallet wrote, not everything somebody remembered to
list. Until 4.7 the address book — the list of people you transact with, stored in **plain text** —
survived it, along with your private transaction notes.

A person deleting their wallet believes it is gone. What stayed behind was precisely the part that
named who they were dealing with.

## 11. Take no cut of a transfer

You pay the network's fee and nothing else. The routing code exists, is tested, and is switched off by
a compile-time zero — deleting machinery from a path that signs real spends is riskier than leaving it
dark.

A cut of every send is the one charge that makes a self-custody wallet feel like a middleman, and a
middleman is a party with something to lose by protecting you.

## 12. Verify against the world, not against yourself

Constants are checked against their source: Dogecoin's dust limit against Dogecoin Core's own fee
documentation, an ERC-20 selector against the standard rather than against this code's output, a
jetton payload against a live response rather than against memory.

Hypotheses get tested before they get fixed. One during this work — that a Bitcoin address would be
accepted as a Bitcoin Cash destination and the coins lost — turned out to be **wrong**, and the guard
written for it was reverted. What stayed was a test pinning the dependency that actually provides the
protection, because that is the kind of thing that vanishes in an upgrade unnoticed.

---

## What this costs

Some of these rules make the wallet look worse than its competitors on a feature list. Rule 2 puts
the limitations of a privacy feature directly under the feature. Rule 1 publishes a list of every
company that sees your addresses. Rule 3 lets an operation fail rather than quietly working through
somebody else's server.

That is the trade, made deliberately. A wallet that oversells itself is fine until the day it is
tested, and the person testing it is not the user.

---

📖 Back to [Documentation Index](docs/INDEX.md)

