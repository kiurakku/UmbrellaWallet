namespace Umbrella.Wallet.Core.Polkadot;

/// <summary>
/// A Substrate runtime's own description of itself (metadata V14): every type, every pallet with its
/// index and call and event types, and the transaction extensions a signed transaction carries.
///
/// A transfer is built from what the RUNNING chain says, not from indices remembered from an older
/// runtime: a pallet index that shifted in an upgrade would otherwise encode as some other pallet's
/// call — which the chain would happily run. So the sender asks this for the Balances pallet's index
/// and its transfer_keep_alive call, and checks the extension list is the one it knows how to fill in;
/// anything else and it refuses to sign.
/// </summary>
public sealed class RuntimeMetadata
{
    public enum Kind { Composite, Variant, Sequence, Array, Tuple, Primitive, Compact, BitSequence }

    public sealed record Field(string? Name, int Type);
    public sealed record Variant(string Name, byte Index, IReadOnlyList<Field> Fields);

    public sealed record TypeDef(
        IReadOnlyList<string> Path,
        Kind Kind,
        IReadOnlyList<Field> Fields,              // composite
        IReadOnlyList<Variant> Variants,          // variant
        int Element,                              // sequence, array, compact: the element type
        uint Length,                              // array
        IReadOnlyList<int> Tuple,                 // tuple
        byte Primitive);                          // primitive: 0 bool, 1 char, 2 str, 3 u8, 4 u16, 5 u32, 6 u64, 7 u128, 8 u256, 9 i8 … 14 i256

    public sealed record Pallet(string Name, byte Index, int? CallType, int? EventType, IReadOnlyDictionary<string, int> PlainStorage,
        IReadOnlyDictionary<string, byte[]> Constants);
    public sealed record Extension(string Identifier, int Type, int AdditionalSigned);

    public IReadOnlyDictionary<int, TypeDef> Types { get; }
    public IReadOnlyList<Pallet> Pallets { get; }
    public byte ExtrinsicVersion { get; }
    public IReadOnlyList<Extension> Extensions { get; }

    private RuntimeMetadata(IReadOnlyDictionary<int, TypeDef> types, IReadOnlyList<Pallet> pallets, byte version, IReadOnlyList<Extension> extensions)
    {
        Types = types;
        Pallets = pallets;
        ExtrinsicVersion = version;
        Extensions = extensions;
    }

    /// <summary>Parses the bytes <c>state_getMetadata</c> returns ("meta", version 14, …).</summary>
    public static RuntimeMetadata Parse(byte[] bytes)
    {
        var r = new ScaleReader(bytes);
        if (r.U32() != 0x6174656D) throw new FormatException("Not runtime metadata.");   // "meta"
        var version = r.U8();
        if (version != 14) throw new FormatException($"Metadata version {version} is not one this wallet reads.");

        var types = new Dictionary<int, TypeDef>();
        var count = r.CompactInt();
        for (var i = 0; i < count; i++)
        {
            var id = r.CompactInt();
            var path = Vec(r, x => x.String());
            Vec(r, x => { x.String(); if (x.Option()) x.CompactInt(); return 0; });   // type params
            types[id] = ReadDef(r, path);
            Vec(r, x => x.String());                                                   // docs
        }

        var pallets = Vec(r, x =>
        {
            var name = x.String();
            var plain = new Dictionary<string, int>(StringComparer.Ordinal);
            if (x.Option())
            {
                x.String();   // prefix
                var entries = x.CompactInt();
                for (var e = 0; e < entries; e++)
                {
                    var entryName = x.String();
                    x.U8();   // modifier
                    switch (x.U8())
                    {
                        case 0: plain[entryName] = x.CompactInt(); break;
                        case 1: Vec(x, y => y.U8()); x.CompactInt(); x.CompactInt(); break;   // hashers, key, value
                        default: throw new FormatException("Unknown storage entry type.");
                    }

                    Vec(x, y => y.U8());          // default value
                    Vec(x, y => y.String());      // docs
                }
            }

            int? calls = x.Option() ? x.CompactInt() : null;
            int? events = x.Option() ? x.CompactInt() : null;
            var constants = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            Vec(x, y =>
            {
                var constName = y.String();
                y.CompactInt();
                constants[constName] = y.Bytes(y.CompactInt());
                Vec(y, z => z.String());
                return 0;
            });
            if (x.Option()) x.CompactInt();   // errors
            var index = x.U8();
            return new Pallet(name, index, calls, events, plain, constants);
        });

        r.CompactInt();   // the extrinsic type
        var extrinsicVersion = r.U8();
        var extensions = Vec(r, x => new Extension(x.String(), x.CompactInt(), x.CompactInt()));
        return new RuntimeMetadata(types, pallets, extrinsicVersion, extensions);
    }

    private static TypeDef ReadDef(ScaleReader r, IReadOnlyList<string> path)
    {
        var tag = r.U8();
        return tag switch
        {
            0 => new TypeDef(path, Kind.Composite, Fields(r), [], 0, 0, [], 0),
            1 => new TypeDef(path, Kind.Variant, [], Vec(r, x =>
            {
                var name = x.String();
                var fields = Fields(x);
                var index = x.U8();
                Vec(x, y => y.String());
                return new Variant(name, index, fields);
            }), 0, 0, [], 0),
            2 => new TypeDef(path, Kind.Sequence, [], [], r.CompactInt(), 0, [], 0),
            3 => ArrayDef(r, path),
            4 => new TypeDef(path, Kind.Tuple, [], [], 0, 0, Vec(r, x => x.CompactInt()), 0),
            5 => new TypeDef(path, Kind.Primitive, [], [], 0, 0, [], r.U8()),
            6 => new TypeDef(path, Kind.Compact, [], [], r.CompactInt(), 0, [], 0),
            7 => new TypeDef(path, Kind.BitSequence, [], [], r.CompactInt(), 0, [r.CompactInt()], 0),
            _ => throw new FormatException($"Unknown type definition {tag}."),
        };
    }

    private static TypeDef ArrayDef(ScaleReader r, IReadOnlyList<string> path)
    {
        var length = r.U32();
        return new TypeDef(path, Kind.Array, [], [], r.CompactInt(), length, [], 0);
    }

    private static List<Field> Fields(ScaleReader r) => Vec(r, x =>
    {
        var name = x.Option() ? x.String() : null;
        var type = x.CompactInt();
        if (x.Option()) x.String();   // type name
        Vec(x, y => y.String());      // docs
        return new Field(name, type);
    });

    private static List<T> Vec<T>(ScaleReader r, Func<ScaleReader, T> item)
    {
        var n = r.CompactInt();
        var list = new List<T>(n);
        for (var i = 0; i < n; i++) list.Add(item(r));
        return list;
    }

    // --- questions a sender asks ----------------------------------------------------------------

    public Pallet? PalletNamed(string name) => Pallets.FirstOrDefault(p => p.Name == name);

    /// <summary>The pallet and call indices of a call, and its fields — or null when the runtime has no
    /// such call.</summary>
    public (byte Pallet, byte Call, IReadOnlyList<Field> Fields)? Call(string pallet, string call)
    {
        if (PalletNamed(pallet) is not { CallType: { } callType } p) return null;
        if (!Types.TryGetValue(callType, out var def) || def.Kind != Kind.Variant) return null;
        return def.Variants.FirstOrDefault(v => v.Name == call) is { } variant
            ? (p.Index, variant.Index, variant.Fields)
            : null;
    }

    /// <summary>True when a type encodes to nothing at all: an empty tuple, or a composite whose fields
    /// all encode to nothing.</summary>
    public bool IsEmpty(int type) => Types.TryGetValue(type, out var d) && d.Kind switch
    {
        Kind.Tuple => d.Tuple.All(IsEmpty),
        Kind.Composite => d.Fields.All(f => IsEmpty(f.Type)),
        Kind.Array => d.Length == 0,
        _ => false,
    };

    /// <summary>Follows single-field composites (newtypes) down to the type they wrap.</summary>
    public TypeDef Resolve(int type)
    {
        var d = Types[type];
        var guard = 0;
        while (d.Kind == Kind.Composite && d.Fields.Count == 1 && guard++ < 16) d = Types[d.Fields[0].Type];
        return d;
    }

    /// <summary>Reads past one value of a type — how an event list is walked to the events that matter
    /// without a decoder for every event there is.</summary>
    public void Skip(ScaleReader r, int type)
    {
        var d = Types[type];
        switch (d.Kind)
        {
            case Kind.Composite:
                foreach (var f in d.Fields) Skip(r, f.Type);
                break;
            case Kind.Variant:
                var index = r.U8();
                var variant = d.Variants.FirstOrDefault(v => v.Index == index)
                              ?? throw new FormatException($"Unknown variant {index}.");
                foreach (var f in variant.Fields) Skip(r, f.Type);
                break;
            case Kind.Sequence:
                var n = r.CompactInt();
                for (var i = 0; i < n; i++) Skip(r, d.Element);
                break;
            case Kind.Array:
                for (var i = 0u; i < d.Length; i++) Skip(r, d.Element);
                break;
            case Kind.Tuple:
                foreach (var t in d.Tuple) Skip(r, t);
                break;
            case Kind.Primitive:
                r.Bytes(d.Primitive switch
                {
                    0 or 3 or 9 => 1,       // bool, u8, i8
                    4 or 10 => 2,           // u16, i16
                    1 or 5 or 11 => 4,      // char, u32, i32
                    6 or 12 => 8,           // u64, i64
                    7 or 13 => 16,          // u128, i128
                    8 or 14 => 32,          // u256, i256
                    2 => r.CompactInt(),    // str
                    _ => throw new FormatException("Unknown primitive."),
                });
                break;
            case Kind.Compact:
                r.Compact();
                break;
            case Kind.BitSequence:
                // A bit sequence is its bit count, then the bits packed into the store type (u8/u16/u32/u64).
                var bits = r.CompactInt();
                var storeBytes = Types[d.Element].Primitive switch { 3 => 1, 4 => 2, 5 => 4, 6 => 8, _ => throw new FormatException("Unknown bit store.") };
                var words = (bits + (storeBytes * 8) - 1) / (storeBytes * 8);
                r.Bytes(words * storeBytes);
                break;
        }
    }
}
