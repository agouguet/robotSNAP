using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Navigability grid sampled from an occupancy image: dark pixels are walls, light ones are walkable.
    ///
    /// The world mapping follows <see cref="OccupancyMapCoordinates"/>: image x grows towards world -x and
    /// image y grows downwards, so the top-left pixel is the (max.x, max.z) corner. The image is also
    /// downsampled, because a 2000 px map is far too large to search cell by cell.
    /// </summary>
    public sealed class OccupancyGrid
    {
        private readonly bool[] _walkable;

        // Optional full-resolution obstacle mask, kept for exact point checks: the coarse grid is the right
        // structure to plan on, but it is too coarse to decide whether a single authored point stands on a wall.
        private readonly bool[] _blockedPixels;
        private readonly int _pixelWidth;
        private readonly int _pixelHeight;

        private OccupancyGrid(
            int width,
            int height,
            int cellPixels,
            Bounds worldBounds,
            bool[] walkable,
            bool[] blockedPixels = null,
            int pixelWidth = 0,
            int pixelHeight = 0)
        {
            Width = width;
            Height = height;
            CellPixels = cellPixels;
            WorldBounds = worldBounds;
            _walkable = walkable;
            _blockedPixels = blockedPixels;
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Source pixels covered by one cell.</summary>
        public int CellPixels { get; }

        public Bounds WorldBounds { get; }
        public bool IsValid => _walkable != null && Width > 0 && Height > 0;
        public float CellSizeX => Width > 0 ? WorldBounds.size.x / Width : 0f;
        public float CellSizeZ => Height > 0 ? WorldBounds.size.z / Height : 0f;
        public int CellCount => _walkable?.Length ?? 0;

        public bool IsWalkable(int x, int y) =>
            _walkable != null && x >= 0 && y >= 0 && x < Width && y < Height && _walkable[y * Width + x];

        public bool IsWorldWalkable(Vector2 world) =>
            TryWorldToCell(world, out int x, out int y) && IsWalkable(x, y);

        /// <summary>True when the grid keeps a full-resolution obstacle mask for exact point checks.</summary>
        public bool HasPixelMask => _blockedPixels != null && _pixelWidth > 0 && _pixelHeight > 0;

        /// <summary>
        /// Exact test of one world point against the pixels of the image, with no cell discretisation. Used to
        /// validate authored points: the coarse planning grid marks a whole 8-pixel cell as a wall, which would
        /// wrongly reject a point standing five centimetres from a wall.
        /// </summary>
        public bool IsPixelWalkable(Vector2 world)
        {
            if (!HasPixelMask)
                return IsWorldWalkable(world);

            Vector2 pixel = WorldToPixel(world);
            int x = Mathf.FloorToInt(pixel.x);
            int y = Mathf.FloorToInt(pixel.y);
            if (x < 0 || y < 0 || x >= _pixelWidth || y >= _pixelHeight)
                return false;

            return !_blockedPixels[y * _pixelWidth + x];
        }

        public Vector2 CellCenter(int x, int y)
        {
            float pixelX = (x + 0.5f) * CellPixels;
            float pixelY = (y + 0.5f) * CellPixels;
            return PixelToWorld(pixelX, pixelY);
        }

        public bool TryWorldToCell(Vector2 world, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (!IsValid)
                return false;

            Vector2 pixel = WorldToPixel(world);
            x = Mathf.FloorToInt(pixel.x / CellPixels);
            y = Mathf.FloorToInt(pixel.y / CellPixels);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        /// <summary>
        /// Continuous cell coordinates of a world point: cell (3, 7) runs from (3, 7) to (4, 8) here, so a
        /// point in its middle answers (3.5, 7.5). Used to follow a segment across the grid cell by cell.
        /// </summary>
        public bool TryWorldToCellF(Vector2 world, out Vector2 cell)
        {
            cell = default;
            if (!IsValid)
                return false;

            Vector2 pixel = WorldToPixel(world);
            cell = new Vector2(pixel.x / CellPixels, pixel.y / CellPixels);
            return cell.x >= 0f && cell.y >= 0f && cell.x <= Width && cell.y <= Height;
        }

        /// <summary>Builds a grid straight from a walkability mask, for tests and tooling.</summary>
        public static OccupancyGrid FromMask(int width, int height, Bounds worldBounds, bool[] walkable, int cellPixels = 1)
        {
            if (width <= 0 || height <= 0 || walkable == null || walkable.Length != width * height)
                return null;

            return new OccupancyGrid(width, height, Mathf.Max(1, cellPixels), worldBounds, walkable);
        }

        /// <summary>
        /// Samples the texture into cells. <paramref name="agentRadius"/> inflates the walls so a path
        /// cannot hug a corner closer than the agent is wide.
        /// </summary>
        public static OccupancyGrid FromTexture(
            Texture2D texture,
            Bounds worldBounds,
            int maxResolution = 400,
            float agentRadius = 0f,
            float darkThreshold = 0.5f,
            bool keepPixelMask = false)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0 ||
                worldBounds.size.x <= 0f || worldBounds.size.z <= 0f)
                return null;

            int resolution = Mathf.Max(32, maxResolution);
            int cellPixels = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(texture.width, texture.height) / (float)resolution));
            int width = Mathf.CeilToInt(texture.width / (float)cellPixels);
            int height = Mathf.CeilToInt(texture.height / (float)cellPixels);
            var walkable = new bool[width * height];

            Color32[] pixels;
            try
            {
                pixels = texture.GetPixels32();
            }
            catch
            {
                // A non readable texture cannot be sampled; callers fall back on straight lines.
                return null;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    walkable[y * width + x] = IsBlockWalkable(
                        pixels,
                        texture.width,
                        texture.height,
                        x,
                        y,
                        cellPixels,
                        darkThreshold);
            }

            bool[] blockedPixels = null;
            if (keepPixelMask)
            {
                blockedPixels = new bool[texture.width * texture.height];
                for (int y = 0; y < texture.height; y++)
                {
                    for (int x = 0; x < texture.width; x++)
                    {
                        blockedPixels[y * texture.width + x] = !IsPixelWalkable(
                            pixels,
                            texture.width,
                            texture.height,
                            x,
                            y,
                            darkThreshold);
                    }
                }
            }

            var grid = new OccupancyGrid(
                width,
                height,
                cellPixels,
                worldBounds,
                walkable,
                blockedPixels,
                texture.width,
                texture.height);
            grid.InflateWalls(agentRadius);
            return grid;
        }

        /// <summary>
        /// Same grid with every wall grown by <paramref name="agentRadius"/>. Used to derive the planning grid
        /// from the raw occupancy grid, so the image is sampled once instead of once per clearance.
        /// </summary>
        public OccupancyGrid WithObstaclesInflatedBy(float agentRadius)
        {
            if (!IsValid)
                return null;

            var copy = new OccupancyGrid(
                Width,
                Height,
                CellPixels,
                WorldBounds,
                (bool[])_walkable.Clone(),
                _blockedPixels,
                _pixelWidth,
                _pixelHeight);
            copy.InflateWalls(agentRadius);
            return copy;
        }

        /// <summary>True when the two grids cover the same area with the same sampling.</summary>
        public bool HasSameSampling(OccupancyGrid other) =>
            other != null &&
            other.Width == Width &&
            other.Height == Height &&
            other.CellPixels == CellPixels &&
            other.WorldBounds == WorldBounds;

        /// <summary>Grows every wall by the agent radius, so a path keeps a body width of clearance.</summary>
        private void InflateWalls(float agentRadius)
        {
            if (agentRadius <= 0f || CellSizeX <= 0f || CellSizeZ <= 0f)
                return;

            int radiusX = Mathf.CeilToInt(agentRadius / CellSizeX);
            int radiusY = Mathf.CeilToInt(agentRadius / CellSizeZ);
            int radius = Mathf.Max(radiusX, radiusY);
            if (radius <= 0)
                return;

            var inflated = new bool[_walkable.Length];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (!_walkable[y * Width + x])
                        continue;

                    bool blocked = false;
                    for (int dy = -radius; dy <= radius && !blocked; dy++)
                    {
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            if (dx * dx + dy * dy > radius * radius)
                                continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                            {
                                blocked = true;
                                break;
                            }
                            if (!_walkable[ny * Width + nx])
                            {
                                blocked = true;
                                break;
                            }
                        }
                    }
                    inflated[y * Width + x] = !blocked;
                }
            }

            for (int index = 0; index < _walkable.Length; index++)
                _walkable[index] = inflated[index];
        }

        /// <summary>
        /// A cell is walkable only when every pixel it covers is clear. Sampling a handful of points let a
        /// thin wall slip between them, which drew paths that crossed the dark pixels of the image: the
        /// occupancy grid has to be conservative, because the map it comes from is a hard floor plan.
        /// </summary>
        private static bool IsBlockWalkable(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int cellX,
            int cellY,
            int cellPixels,
            float darkThreshold)
        {
            int originX = cellX * cellPixels;
            int originY = cellY * cellPixels;
            int lastX = Mathf.Min(originX + cellPixels - 1, textureWidth - 1);
            int lastY = Mathf.Min(originY + cellPixels - 1, textureHeight - 1);

            for (int y = originY; y <= lastY; y++)
            {
                for (int x = originX; x <= lastX; x++)
                {
                    if (!IsPixelWalkable(pixels, textureWidth, textureHeight, x, y, darkThreshold))
                        return false;
                }
            }

            return true;
        }

        private static bool IsPixelWalkable(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int x,
            int y,
            float darkThreshold)
        {
            if (x < 0 || y < 0 || x >= textureWidth || y >= textureHeight)
                return false;

            // Texture rows start at the bottom in Unity, the grid rows start at the top of the image.
            int row = textureHeight - 1 - y;
            Color32 pixel = pixels[row * textureWidth + x];
            float luminance = (pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f) / 255f;
            return luminance >= darkThreshold;
        }

        private Vector2 WorldToPixel(Vector2 world)
        {
            float imageX = Mathf.Approximately(WorldBounds.size.x, 0f)
                ? 0.5f
                : (WorldBounds.max.x - world.x) / WorldBounds.size.x;
            float imageY = Mathf.Approximately(WorldBounds.size.z, 0f)
                ? 0.5f
                : (world.y - WorldBounds.min.z) / WorldBounds.size.z;

            float textureWidth = Width * CellPixels;
            float textureHeight = Height * CellPixels;
            // Rows of the grid are counted from the top of the image, like the preview of the editor and like
            // <see cref="OccupancyMapCoordinates"/>. The vertical flip of Unity's own pixel buffers is already
            // handled once, when the walkability masks are read out of the texture.
            return new Vector2(imageX * textureWidth, imageY * textureHeight);
        }

        private Vector2 PixelToWorld(float pixelX, float pixelY)
        {
            float textureWidth = Width * CellPixels;
            float textureHeight = Height * CellPixels;
            float imageX = pixelX / textureWidth;
            float imageY = pixelY / textureHeight;
            return new Vector2(
                WorldBounds.max.x - imageX * WorldBounds.size.x,
                WorldBounds.min.z + imageY * WorldBounds.size.z);
        }
    }
}
