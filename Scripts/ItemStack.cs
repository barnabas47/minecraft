using Godot;

namespace Minecraft;

public struct ItemStack
{
	public BlockType Type;
	public int Count;

	public ItemStack(BlockType type, int count = 1)
	{
		Type = type;
		Count = count;
	}

	public static ItemStack Empty => new ItemStack(BlockType.Air, 0);
	public bool IsEmpty => Type == BlockType.Air || Count <= 0;
}
