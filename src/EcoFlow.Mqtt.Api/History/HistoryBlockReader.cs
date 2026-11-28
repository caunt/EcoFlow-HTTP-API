namespace EcoFlow.Mqtt.Api.History;

// Reads blocks one after another, reusing the decoder state of every field between them.
internal sealed class HistoryBlockReader(int fieldCount)
{
    private readonly long[] _mantissas = new long[fieldCount];
    private readonly byte[] _scales = new byte[fieldCount];

    // The block being read and how far into each of its three streams the reader is.
    private ReadOnlyMemory<byte> _data;
    private int _fieldsPosition;
    private int _timesPosition;
    private int _valuesPosition;

    // The change read last, which the next one is measured from.
    private int _fieldId;
    private int _remainingChanges;
    private long _time;

    // Reads the keyframe into the values, indexed by field id. Fields that did not exist yet are left missing.
    public void Open(HistoryBlock block, Span<HistoryValue> values)
    {
        Array.Clear(_mantissas);
        Array.Clear(_scales);

        // The three streams follow each other in the data.
        _data = block.Decompress();
        _valuesPosition = 0;
        _fieldsPosition = block.ValuesLength;
        _timesPosition = block.ValuesLength + block.FieldsLength;

        // The first change is measured from the start of the block.
        _remainingChanges = block.ChangeCount;
        _fieldId = 0;
        _time = block.StartTime;

        values.Clear();

        for (int fieldId = 0; fieldId < block.KeyframeCount; fieldId++)
        {
            values[fieldId] = HistoryCodec.ReadValue(_data.Span,
                ref _valuesPosition,
                ref _mantissas[fieldId],
                ref _scales[fieldId]);
        }
    }

    public bool TryRead(out long time, out int fieldId, out HistoryValue value)
    {
        if (_remainingChanges is 0)
        {
            time = 0;
            fieldId = 0;
            value = default;

            return false;
        }

        ReadOnlySpan<byte> data = _data.Span;

        _remainingChanges--;
        _time += long.CreateChecked(HistoryCodec.ReadVariableInteger(data, ref _timesPosition));
        _fieldId += int.CreateChecked(HistoryCodec.DecodeSigned(HistoryCodec.ReadVariableInteger(data, ref _fieldsPosition)));

        // Time and field are stored as the difference from the change before.
        time = _time;
        fieldId = _fieldId;
        value = HistoryCodec.ReadValue(data, ref _valuesPosition, ref _mantissas[_fieldId], ref _scales[_fieldId]);

        return true;
    }
}
