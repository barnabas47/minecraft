# Minecraft Clone in Godot 4 (C#)

A voxel-based 3D sandbox game built from scratch in Godot Engine 4 using C# and .NET 8.0.

> [!NOTE]
> Ez egy kezdetleges stádiumban lévő (WIP) hobbi projekt, amely a Minecraft alapvető mechanikáit (bányászat, építés, fizikai droppok, kraftolás, kemence/sütés) valósítja meg zökkenőmentes többszálú világ-generálással.

---

## 🎮 Főbb Funkciók (Key Features)

- **Aszinkron & Többszálú Világbetöltés**: A domborzat-generálás, a fák beültetése és a 3D mesh adatok kiszámítása teljesen külön háttérszálakon (`Task.Run`) történik, így a mozgás és a chunkok betöltése nem okoz akadozást (zero-lag main thread).
- **Procedurális Világ**: Simplex zajgenerátor alapú domborzat hullámzó dombokkal és különböző mélységekben elhelyezkedő ércekkel (szén, vas, arany, gyémánt).
- **Fizikai Tárgy-eldobás (Dropped Items)**: A kibányászott blokkok 3D fizikai testként esnek a földre, ütköznek a talajjal, lebegnek, és mágnesesen a közelben lévő játékos felé vonzódnak.
- **Teljes Inventory & Hotbar**: Működő UI a tárgyak tárolására, mozgatására, halmozására (stacking) és a hotbar slotok kiválasztására.
- **Interaktív Blokkok**:
  - **Crafting Table**: Barkácsoló felület recepekkel (pl. fa és kő eszközök, bot, kemence).
  - **Furnace (Kemence)**: Sütő felület üzemanyag-fogyasztással (szén, fa, botok), sütési idővel és progress barral (pl. nyers hús megsütése, kő kiégetése).
- **Optimalizált Ütközések**: Henger (Cylinder) alapú játékos test a zökkenőmentes átlós haladáshoz és a beszorulások elkerülésére.
- **Erőforrás Gyorsítótár (Material Cache)**: Statikus anyag- és textúra-gyorsítótár a lemezműveletek minimalizálására.

---

## 🛠️ Technológiai Stack (Tech Stack)

- **Játékmotor**: Godot Engine 4.x (Mono/C#)
- **Fejlesztői Platform**: .NET 8.0
- **Algoritmusok**: FastNoiseLite a domborzathoz
- **Renderelés**: Voxel-alapú egyedi háló generálás (SurfaceTool)

---

## 🚀 Futtatás (Getting Started)

### Követelmények:
- [Godot Engine 4.x (.NET/Mono verzió)](https://godotengine.org/download)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Lépések:
1. Klónozd a repót:
   ```bash
   git clone https://github.com/barnabas47/minecraft.git
   ```
2. Nyisd meg a projektet a Godot editorban (`project.godot`).
3. Futtasd a projektet közvetlenül a Godot editorból (F5), vagy építsd újra parancssorból:
   ```bash
   dotnet build
   ```

---

## 🎹 Irányítás (Controls)

- **Mozgás**: `W`, `A`, `S`, `D`
- **Ugrás**: `Space`
- **Kamera**: Egér (belső nézet)
- **Bányászat (Blokk kiütése)**: Egér bal klikk (tartva)
- **Építés (Blokk lerakása)**: Egér jobb klikk
- **Inventory megnyitása**: `E`
- **Interakció (Crafting / Furnace)**: Egér jobb klikk az adott blokkon
- **Tárgy kiválasztása**: Egér görgő vagy a `1-9` számbillentyűk

---

## 📸 Képek (Images)
<img width="1148" height="644" alt="image" src="https://github.com/user-attachments/assets/deb656d5-3e80-4dd3-a6e8-021d7a8125c5" />
