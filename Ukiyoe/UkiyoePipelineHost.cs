using ComputeWeave;

namespace Ukiyoe;

[ComputeResourceGroup]
internal sealed partial class UkiyoeGridResources
{
    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> ColorIn { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> ColorA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float4> ColorB { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Gray { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> GradientMagnitude { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Response { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<float> Line { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float2> TangentA { get; }

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite)]
    internal ReadWriteBuffer<Float2> TangentB { get; }
}

[ComputePipelineHost("_device", 1)]
internal sealed partial class UkiyoePipelineHost
{
    private readonly GraphicsDevice _device;

    [ComputePipelineResource(ComputeResourceAccess.ReadWrite, ComputeResourceRecovery.Recompute)]
    private readonly ComputeResourceGroupSlot<UkiyoeGridResources> _grid = new();

    [ComputePipeline]
    private void RecordFullPipeline(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] UkiyoeGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int width,
        int height,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived,
        in UkiyoePipeline.Parameters parameters)
    {
        _ = _device;

        RecordSilhouetteStage(in context, grid, source, 0, 0, width, height, gridWidth, gridHeight, in derived);
        RecordStructureStage(in context, grid, scratch, gridWidth, gridHeight, in derived);
        RecordRenderStage(in context, grid, output, new UkiyoePipeline.PixelRect(0, 0, width, height), gridWidth, gridHeight, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordSilhouetteAndMaskHash(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] UkiyoeGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived)
    {
        _ = _device;

        RecordSilhouetteStage(in context, grid, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, in derived);
        RecordMaskHashStage(in context, grid, scratch, gridWidth, gridHeight);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedSilhouetteAndMaskHash(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] UkiyoeGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> source,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived)
    {
        _ = _device;

        RecordSilhouetteStage(in context, grid, source, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, in derived);
        RecordMaskHashStage(in context, grid, scratch, gridWidth, gridHeight);
    }

    [ComputePipeline]
    [ComputeInterop]
    private void RecordSharedRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] UkiyoeGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite, Sharing = ComputeResourceSharing.External)] ReadWriteTexture2D<Bgra32, Float4> output,
        in UkiyoePipeline.PixelRect rect,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived,
        in UkiyoePipeline.Parameters parameters)
    {
        _ = _device;

        RecordRenderStage(in context, grid, output, rect, gridWidth, gridHeight, in derived, in parameters);
    }

    [ComputePipeline]
    private void RecordStructure(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] UkiyoeGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteBuffer<int> scratch,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived)
    {
        _ = _device;

        RecordStructureStage(in context, grid, scratch, gridWidth, gridHeight, in derived);
    }

    [ComputePipeline]
    private void RecordRender(
        in ComputeContext context,
        [ComputeOwnedResource(nameof(_grid))] UkiyoeGridResources grid,
        [ComputeResource(ComputeResourceAccess.ReadWrite)] ReadWriteTexture2D<Bgra32, Float4> output,
        in UkiyoePipeline.PixelRect rect,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived,
        in UkiyoePipeline.Parameters parameters)
    {
        _ = _device;

        RecordRenderStage(in context, grid, output, rect, gridWidth, gridHeight, in derived, in parameters);
    }

    private static void RecordSilhouetteStage(
        in ComputeContext context,
        UkiyoeGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> source,
        int sourceOffsetX,
        int sourceOffsetY,
        int sourceWidth,
        int sourceHeight,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived)
    {
        context.For(gridWidth, gridHeight, new SilhouetteShader(
            source, grid.ColorIn, grid.Gray, sourceOffsetX, sourceOffsetY, sourceWidth, sourceHeight, gridWidth, gridHeight, derived.CellSize));
        context.Barrier(grid.ColorIn);
        context.Barrier(grid.Gray);
    }

    private static void RecordMaskHashStage(
        in ComputeContext context,
        UkiyoeGridResources grid,
        ReadWriteBuffer<int> scratch,
        int gridWidth,
        int gridHeight)
    {
        context.For(1, new MaskHashResetShader(scratch));
        context.Barrier(scratch);
        context.For(gridWidth, gridHeight, new MaskHashShader(grid.ColorIn, scratch, gridWidth, gridHeight));
        context.Barrier(scratch);
    }

    private static void RecordStructureStage(
        in ComputeContext context,
        UkiyoeGridResources grid,
        ReadWriteBuffer<int> scratch,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived)
    {
        context.For(1, new InitScratchShader(scratch));
        context.Barrier(scratch);
        context.For(gridWidth, gridHeight, new GradientShader(grid.Gray, grid.TangentA, grid.GradientMagnitude, scratch, gridWidth, gridHeight));
        context.Barrier(grid.TangentA);
        context.Barrier(grid.GradientMagnitude);
        context.Barrier(scratch);

        var tangentIn = grid.TangentA;
        var tangentOut = grid.TangentB;
        for (var iteration = 0; iteration < derived.EtfIterations; iteration++)
        {
            context.For(gridWidth, gridHeight, new EtfShader(tangentIn, tangentOut, grid.GradientMagnitude, scratch, gridWidth, gridHeight));
            context.Barrier(tangentOut);
            (tangentIn, tangentOut) = (tangentOut, tangentIn);
        }
        var tangent = tangentIn;

        context.For(gridWidth, gridHeight, new CopyColorShader(grid.ColorIn, grid.ColorA, gridWidth, gridHeight));
        context.Barrier(grid.ColorA);
        var flattenDispatchWidth = (gridWidth + UkiyoeSettings.FlattenGroupDim - 1) & ~(UkiyoeSettings.FlattenGroupDim - 1);
        var flattenDispatchHeight = (gridHeight + UkiyoeSettings.FlattenGroupDim - 1) & ~(UkiyoeSettings.FlattenGroupDim - 1);
        var iterateIn = grid.ColorA;
        var iterateOut = grid.ColorB;
        for (var iteration = 0; iteration < derived.FlattenIterations; iteration++)
        {
            context.For(flattenDispatchWidth, flattenDispatchHeight, new FlattenShader(
                grid.ColorIn, iterateIn, iterateOut, grid.GradientMagnitude, scratch, gridWidth, gridHeight, derived.FlattenBeta));
            context.Barrier(iterateOut);
            (iterateIn, iterateOut) = (iterateOut, iterateIn);
        }

        for (var iteration = 0; iteration < derived.FdogIterations; iteration++)
        {
            context.For(gridWidth, gridHeight, new DogShader(
                grid.Gray, tangent, grid.Response, gridWidth, gridHeight, derived.LineSigmaCells, derived.DogExtent));
            context.Barrier(grid.Response);
            context.For(gridWidth, gridHeight, new FlowAccumulateShader(
                grid.Response, tangent, grid.Line, gridWidth, gridHeight, derived.FlowSigma, derived.FlowExtent));
            context.Barrier(grid.Line);
            if (iteration < derived.FdogIterations - 1)
            {
                context.For(gridWidth, gridHeight, new SuperimposeShader(grid.Line, grid.Gray, gridWidth, gridHeight, derived.LineThreshold));
                context.Barrier(grid.Gray);
            }
        }
    }

    private static void RecordRenderStage(
        in ComputeContext context,
        UkiyoeGridResources grid,
        ReadWriteTexture2D<Bgra32, Float4> output,
        in UkiyoePipeline.PixelRect rect,
        int gridWidth,
        int gridHeight,
        in UkiyoePipeline.DerivedValues derived,
        in UkiyoePipeline.Parameters parameters)
    {
        context.For(rect.Width, rect.Height, new RenderShader(
            grid.ColorA, grid.Line, output,
            rect.X, rect.Y, rect.Width, rect.Height, gridWidth, gridHeight,
            derived.CellSize,
            UkiyoeSettings.ClampPaletteLevels(parameters.PaletteLevels),
            derived.ShiftPixels,
            Math.Clamp(parameters.Baren, 0f, 1f),
            Math.Clamp(parameters.Paper, 0f, 1f),
            derived.LineThreshold,
            Math.Clamp(parameters.LineStrength, 0f, 1f),
            parameters.LineColorR,
            parameters.LineColorG,
            parameters.LineColorB,
            parameters.Seed));
    }
}
