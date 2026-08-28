namespace Source.ShaderAPI.Veldrid;

public static class SpirvBindings
{
	const uint MagicNumber = 0x07230203;
	const uint OpDecorate = 71;
	const uint DecorationDescriptorSet = 34;
	const uint DecorationBinding = 33;

	public readonly record struct Resource(uint Set, uint Binding);

	public static void Read(ReadOnlySpan<byte> spirv, HashSet<Resource> into) {
		ReadOnlySpan<uint> words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(spirv);
		if (words.Length < 5 || words[0] != MagicNumber)
			return;

		Dictionary<uint, uint> sets = [];
		Dictionary<uint, uint> bindings = [];

		int i = 5;
		while (i < words.Length) {
			uint instruction = words[i];
			uint opcode = instruction & 0xFFFF;
			int wordCount = (int)(instruction >> 16);

			if (wordCount <= 0 || i + wordCount > words.Length)
				break;

			if (opcode == OpDecorate && wordCount >= 4) {
				uint target = words[i + 1];
				uint decoration = words[i + 2];
				uint value = words[i + 3];

				if (decoration == DecorationDescriptorSet)
					sets[target] = value;
				else if (decoration == DecorationBinding)
					bindings[target] = value;
			}

			i += wordCount;
		}

		foreach ((uint target, uint binding) in bindings) {
			sets.TryGetValue(target, out uint set);
			into.Add(new Resource(set, binding));
		}
	}
}
