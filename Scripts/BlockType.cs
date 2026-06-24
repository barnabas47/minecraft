using Godot;
using System;

namespace Minecraft;

public enum BlockType : byte
{
	Air            = 0,
	Grass          = 1,
	Dirt           = 2,
	Stone          = 3,
	Wood           = 4,
	Leaves         = 5,
	Brick          = 6,
	Planks         = 7,
	Glass          = 8,
	CoalOre        = 9,
	IronOre        = 10,
	GoldOre        = 11,
	DiamondOre     = 12,
	Cobblestone    = 13,
	CraftingTable  = 14,
	WoodenPickaxe  = 15,
	StonePickaxe   = 16,
	IronPickaxe    = 17,
	DiamondPickaxe = 18,
	WoodenAxe      = 19,
	StoneAxe       = 20,
	IronAxe        = 21,
	DiamondAxe     = 22,
	WoodenShovel   = 23,
	StoneShovel    = 24,
	IronShovel     = 25,
	DiamondShovel  = 26,
	Furnace        = 27,
	Stick          = 28,
	WoodenSword    = 29,
	StoneSword     = 30,
	IronSword      = 31,
	GoldSword      = 32,
	DiamondSword   = 33,
	WoodenHoe      = 34,
	StoneHoe       = 35,
	IronHoe        = 36,
	GoldHoe        = 37,
	DiamondHoe     = 38,
	Apple          = 39,
	RawPorkchop    = 40,
	CookedPorkchop = 41,
	GoldPickaxe    = 42,
	GoldAxe        = 43,
	GoldShovel     = 44
}

public static class BlockDatabase
{
	// terrain.png: 256x256px, 8 oszlop x 8 sor, 32x32px tile-ok
	// Index = sor*8 + oszlop
	//
	//                             L   R   F   B  Top Bot
	private static readonly int[] GrassSides       = { 1,  1,  1,  1,  0,  2 };
	private static readonly int[] DirtSides        = { 2,  2,  2,  2,  2,  2 };
	private static readonly int[] StoneSides       = { 3,  3,  3,  3,  3,  3 };
	private static readonly int[] WoodSides        = { 4,  4,  4,  4,  5,  5 };
	private static readonly int[] LeavesSides      = { 6,  6,  6,  6,  6,  6 };
	private static readonly int[] BrickSides       = { 8,  8,  8,  8,  8,  8 };
	private static readonly int[] PlanksSides      = { 9,  9,  9,  9,  9,  9 };
	private static readonly int[] GlassSides       = { 10, 10, 10, 10, 10, 10 };
	private static readonly int[] CoalOreSides     = { 11, 11, 11, 11, 11, 11 };
	private static readonly int[] IronOreSides     = { 12, 12, 12, 12, 12, 12 };
	private static readonly int[] GoldOreSides     = { 13, 13, 13, 13, 13, 13 };
	private static readonly int[] DiamondOreSides  = { 14, 14, 14, 14, 14, 14 };
	private static readonly int[] CobblestoneSides = { 15, 15, 15, 15, 15, 15 };
	private static readonly int[] CraftingTableSides = { 17, 17, 17, 17, 16, 9 };
	private static readonly int[] FurnaceSides     = { 19, 19, 19, 18, 19, 19 };

	// Atlas paraméterek – Új 256x256-os képhez 8 sor
	public const int AtlasCols = 8;
	public const int AtlasRows = 8;

	public static int GetTextureIndex(BlockType blockType, int faceIndex)
	{
		return blockType switch
		{
			BlockType.Grass       => GrassSides[faceIndex],
			BlockType.Dirt        => DirtSides[faceIndex],
			BlockType.Stone       => StoneSides[faceIndex],
			BlockType.Wood        => WoodSides[faceIndex],
			BlockType.Leaves      => LeavesSides[faceIndex],
			BlockType.Brick       => BrickSides[faceIndex],
			BlockType.Planks      => PlanksSides[faceIndex],
			BlockType.Glass       => GlassSides[faceIndex],
			BlockType.CoalOre     => CoalOreSides[faceIndex],
			BlockType.IronOre     => IronOreSides[faceIndex],
			BlockType.GoldOre     => GoldOreSides[faceIndex],
			BlockType.DiamondOre  => DiamondOreSides[faceIndex],
			BlockType.Cobblestone => CobblestoneSides[faceIndex],
			BlockType.CraftingTable => CraftingTableSides[faceIndex],
			BlockType.Furnace     => FurnaceSides[faceIndex],
			_ => 0
		};
	}

	public static bool IsTransparent(BlockType blockType)
	{
		return blockType == BlockType.Air || 
			   blockType == BlockType.Leaves || 
			   blockType == BlockType.Glass;
	}

	public static string GetToolCategory(BlockType tool)
	{
		string name = tool.ToString();
		if (name.Contains("Pickaxe")) return "pickaxe";
		if (name.Contains("Axe")) return "axe";
		if (name.Contains("Shovel")) return "shovel";
		return "none";
	}

	public static int GetToolTier(BlockType tool)
	{
		string name = tool.ToString();
		if (name.StartsWith("Wooden")) return 1;
		if (name.StartsWith("Stone")) return 2;
		if (name.StartsWith("Iron") || name.StartsWith("Gold")) return 3;
		if (name.StartsWith("Diamond")) return 4;
		return 0;
	}

	public static string GetRequiredToolCategory(BlockType block)
	{
		return block switch
		{
			BlockType.Stone or BlockType.Cobblestone or BlockType.Brick or
			BlockType.CoalOre or BlockType.IronOre or BlockType.GoldOre or BlockType.DiamondOre or BlockType.Furnace => "pickaxe",
			
			BlockType.Wood or BlockType.Planks or BlockType.CraftingTable => "axe",
			
			BlockType.Grass or BlockType.Dirt => "shovel",
			
			_ => "none"
		};
	}

	public static int GetMinimumToolTier(BlockType block)
	{
		return block switch
		{
			BlockType.IronOre => 2,
			BlockType.GoldOre or BlockType.DiamondOre => 3,
			_ => 1
		};
	}

	public static bool CanDropItem(BlockType block, BlockType tool)
	{
		if (block == BlockType.Leaves || block == BlockType.Glass) return false;

		string reqCategory = GetRequiredToolCategory(block);
		if (reqCategory == "none") return true;

		string toolCategory = GetToolCategory(tool);
		int toolTier = GetToolTier(tool);
		int minTier = GetMinimumToolTier(block);

		if (reqCategory == "pickaxe")
		{
			return toolCategory == "pickaxe" && toolTier >= minTier;
		}

		return true;
	}

	public static float GetMiningSpeedMultiplier(BlockType block, BlockType tool)
	{
		string reqCategory = GetRequiredToolCategory(block);
		if (reqCategory == "none") return 1.0f;

		string toolCategory = GetToolCategory(tool);
		if (toolCategory != reqCategory) return 1.0f;

		int tier = GetToolTier(tool);
		return tier switch
		{
			1 => 2.0f,
			2 => 4.0f,
			3 => 6.0f,
			4 => 8.0f,
			_ => 1.0f
		};
	}
}
