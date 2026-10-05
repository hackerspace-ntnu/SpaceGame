// Who each netcode client id is: a display name and a Unity account id.
//
// In the world a speaking client id is matched to a body through PlayerIdentity.OwnerClientId. In
// the lobby there are no bodies -- persistentScene has not loaded, so nothing is spawned -- and the
// lobby's own roster is keyed by UGS player id and slot, neither of which is a netcode client id.
// So voice carries its own small table: each peer tells the server who it is, and the server hands
// the whole table to everyone whenever it changes.
//
// The account id does two jobs. It is what per-person voice levels are remembered by (see
// VoicePeerLevels), and in the lobby it is what finds a speaker's slot, and so their team.
//
// ## Trust
//
// The server files an identity under the client id it ARRIVED FROM, never an id written inside it,
// so a client can only ever describe itself (GDC-L1-MP-0004: trust the server, not the client). The
// account id is still self-reported — nothing here can prove it — which is why it is only ever used
// for things a liar gains nothing from: which volume YOU hear them at, and which colour their row is.
// It is also sanitised to the shape a UGS id actually has, because it becomes part of a PlayerPrefs
// key on every machine that hears it.
//
// Pure encode/decode over byte arrays, so the format is pinned by an EditMode test.
using System;
using System.Collections.Generic;
using System.Text;

namespace SpaceGame.Voice
{
    /// <summary>The client-id-to-identity table, and the one identity a peer announces.</summary>
    public static class VoiceRoster
    {
        /// <summary>One peer's identity as the table carries it.</summary>
        public readonly struct Entry
        {
            public readonly string Name;
            public readonly string AccountId;

            public Entry(string name, string accountId)
            {
                Name = name ?? string.Empty;
                AccountId = accountId ?? string.Empty;
            }
        }

        /// <summary>
        /// Longest encoded name. GameSettings caps a name at 20 characters, which is at most 60
        /// bytes of UTF-8; this leaves headroom and still fits the one-byte length prefix.
        /// </summary>
        public const int MaxNameBytes = 96;

        /// <summary>A UGS player id is well under this. Longer is not one.</summary>
        public const int MaxAccountBytes = 64;

        /// <summary>More than any session holds. A table claiming more is corrupt, not large.</summary>
        public const int MaxEntries = 64;

        private const int CountBytes = 2;

        /// <summary>A buffer this size holds any valid announcement.</summary>
        public const int MaxIdentityBytes = 1 + MaxNameBytes + 1 + MaxAccountBytes;

        /// <summary>A buffer this size holds any valid table.</summary>
        public const int MaxEncodedBytes = CountBytes + MaxEntries * (VoiceFormat.ClientIdBytes + MaxIdentityBytes);

        // ---------------------------------------------------------------- one identity

        /// <summary>
        /// Writes one peer's announcement — name, then account id — and returns its length.
        /// <paramref name="into"/> must hold at least <see cref="MaxIdentityBytes"/>.
        /// </summary>
        public static int EncodeIdentity(string name, string accountId, byte[] into) =>
            WriteIdentity(new Entry(name, accountId), into, 0);

        /// <summary>Reads one announcement. False for anything malformed.</summary>
        public static bool DecodeIdentity(byte[] data, int length, out Entry entry)
        {
            entry = default;
            if (data == null || length <= 0 || length > data.Length) return false;

            int at = 0;
            return ReadIdentity(data, length, ref at, out entry) && at == length;
        }

        // ---------------------------------------------------------------------- table

        /// <summary>
        /// Writes <paramref name="table"/> into <paramref name="into"/>, which must be at least
        /// <see cref="MaxEncodedBytes"/> long, and returns the byte count. Entries past
        /// <see cref="MaxEntries"/> are dropped rather than overrunning the buffer.
        /// </summary>
        public static int Encode(IReadOnlyDictionary<ulong, Entry> table, byte[] into)
        {
            int count = table == null ? 0 : Math.Min(table.Count, MaxEntries);
            into[0] = (byte)count;
            into[1] = (byte)(count >> 8);

            int at = CountBytes;
            if (count == 0) return at;

            int written = 0;
            foreach (KeyValuePair<ulong, Entry> row in table)
            {
                if (written == count) break;

                VoiceFormat.WriteClientId(into, at, row.Key);
                at += VoiceFormat.ClientIdBytes;
                at = WriteIdentity(row.Value, into, at);

                written++;
            }

            return at;
        }

        /// <summary>
        /// Reads a table into <paramref name="into"/>. Returns false, with <paramref name="into"/>
        /// left empty, for anything malformed — a corrupt packet must never leave a half-applied
        /// table in which some identities are fresh and the rest belong to whoever held the id before.
        /// </summary>
        public static bool Decode(byte[] data, int length, Dictionary<ulong, Entry> into)
        {
            into.Clear();
            if (data == null || length < CountBytes || length > data.Length) return false;

            int count = data[0] | (data[1] << 8);
            if (count > MaxEntries) return false;

            int at = CountBytes;

            for (int i = 0; i < count; i++)
            {
                if (at + VoiceFormat.ClientIdBytes > length) return Fail(into);

                ulong id = VoiceFormat.ReadClientId(data, at);
                at += VoiceFormat.ClientIdBytes;

                if (!ReadIdentity(data, length, ref at, out Entry entry)) return Fail(into);
                into[id] = entry;
            }

            // Strict about trailing bytes: they mean the two ends disagree about the format, which
            // is a bug worth failing on rather than a packet worth half-trusting.
            return at == length || Fail(into);
        }

        // ------------------------------------------------------------------ sanitising

        /// <summary>
        /// A name as UTF-8, cut to <see cref="MaxNameBytes"/> on a character boundary — so a
        /// truncated multi-byte character, or half a surrogate pair, never arrives as a
        /// replacement glyph at the other end.
        /// </summary>
        public static byte[] NameBytes(string name)
        {
            string text = name ?? string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(text);

            while (bytes.Length > MaxNameBytes && text.Length > 0)
            {
                text = text.Substring(0, text.Length - 1);
                if (text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1]))
                    text = text.Substring(0, text.Length - 1);

                bytes = Encoding.UTF8.GetBytes(text);
            }

            return bytes;
        }

        /// <summary>
        /// <paramref name="raw"/> if it has the shape of a UGS player id — letters, digits, '-' and
        /// '_', at most <see cref="MaxAccountBytes"/> long — otherwise empty. Refused rather than
        /// repaired: this becomes part of a PlayerPrefs key, and a "cleaned" id would be someone
        /// else's.
        /// </summary>
        public static string CleanAccountId(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxAccountBytes) return string.Empty;

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                               (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!allowed) return string.Empty;
            }

            return raw;
        }

        // ------------------------------------------------------------------- internals

        private static int WriteIdentity(Entry entry, byte[] into, int at)
        {
            byte[] name = NameBytes(entry.Name);
            byte[] account = Encoding.ASCII.GetBytes(CleanAccountId(entry.AccountId));

            into[at++] = (byte)name.Length;
            Array.Copy(name, 0, into, at, name.Length);
            at += name.Length;

            into[at++] = (byte)account.Length;
            Array.Copy(account, 0, into, at, account.Length);
            at += account.Length;

            return at;
        }

        private static bool ReadIdentity(byte[] data, int length, ref int at, out Entry entry)
        {
            entry = default;

            if (at + 1 > length) return false;
            int nameLength = data[at++];
            if (nameLength > MaxNameBytes || at + nameLength > length) return false;

            string name = Encoding.UTF8.GetString(data, at, nameLength);
            at += nameLength;

            if (at + 1 > length) return false;
            int accountLength = data[at++];
            if (accountLength > MaxAccountBytes || at + accountLength > length) return false;

            string account = CleanAccountId(Encoding.ASCII.GetString(data, at, accountLength));
            at += accountLength;

            entry = new Entry(name, account);
            return true;
        }

        private static bool Fail(Dictionary<ulong, Entry> into)
        {
            into.Clear();
            return false;
        }
    }
}
