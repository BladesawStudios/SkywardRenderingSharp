namespace SkywardRenderingSharp.Model;

/// <summary>How one attribute is stored in the primitive stream: absent, 8-bit index or 16-bit index (Size is its byte width).</summary>
readonly record struct AttributeMode(int Size)
{
    public static AttributeMode From(uint mode) => mode switch
    {
        0 => new(0), 2 => new(1), 3 => new(2),
        // TODO: direct attributes (mode 1) are not decoded; the game's models are all indexed, and this stops a partial implementation from guessing sizes.
        _ => throw new NotSupportedException("Direct vertex attributes are not supported."),
    };
}

/// <summary>The per-vertex byte layout a polygon's CP vertex descriptor (VCD low/high) implies.</summary>
readonly record struct VertexLayout(int PositionMatrixBytes, int TexMatrixBytes, AttributeMode Position, AttributeMode Normal,
    AttributeMode Color0, AttributeMode Color1, AttributeMode[] Uv)
{
    public int Stride => PositionMatrixBytes + TexMatrixBytes + Position.Size + Normal.Size + Color0.Size + Color1.Size + Uv.Sum(u => u.Size);

    public static VertexLayout From(uint lo, uint hi)
    {
        int texMatrices = 0;
        for (int i = 0; i < 8; i++) texMatrices += (int)(lo >> (1 + i) & 1);
        var uv = new AttributeMode[8];
        for (int i = 0; i < 8; i++) uv[i] = AttributeMode.From(hi >> (2 * i) & 3);
        return new VertexLayout((int)(lo & 1), texMatrices, AttributeMode.From(lo >> 9 & 3), AttributeMode.From(lo >> 11 & 3),
            AttributeMode.From(lo >> 13 & 3), AttributeMode.From(lo >> 15 & 3), uv);
    }
}

/// <summary>The array indices a polygon object picks from the model's attribute arrays (-1 when unused).</summary>
readonly struct ArrayIds
{
    public int Position { get; }
    public int Normal { get; }
    public int Color0 { get; }
    public int[] Uv { get; }

    public ArrayIds(Brres.BigEndian f, int at)
    {
        Position = f.S16(at); Normal = f.S16(at + 2); Color0 = f.S16(at + 4);
        Uv = Enumerable.Range(0, 8).Select(i => f.S16(at + 8 + 2 * i)).ToArray();
    }
}
