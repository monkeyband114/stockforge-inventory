using Microsoft.AspNetCore.Mvc;
using StockForge.API.Authorization;
using StockForge.Application.Catalog;
using StockForge.Application.Common;
using StockForge.Application.Security;
using StockForge.Application.Warehouses;

namespace StockForge.API.Controllers;

[ApiController]
[Route("api/warehouses")]
public sealed class WarehousesController(WarehouseService warehouses) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Warehouses.View)]
    public Task<IReadOnlyList<WarehouseDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        warehouses.ListAsync(includeInactive, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Warehouses.View)]
    public Task<WarehouseDto> Get(Guid id, CancellationToken ct) => warehouses.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Permissions.Warehouses.Manage)]
    public async Task<ActionResult<WarehouseDto>> Create(CreateWarehouseRequest request, CancellationToken ct)
    {
        var warehouse = await warehouses.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = warehouse.Id }, warehouse);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Warehouses.Manage)]
    public Task<WarehouseDto> Update(Guid id, UpdateWarehouseRequest request, CancellationToken ct) => warehouses.UpdateAsync(id, request, ct);
}

[ApiController]
[Route("api/units")]
public sealed class UnitsController(UnitService units) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Products.View)]
    public Task<IReadOnlyList<UnitDto>> List(CancellationToken ct) => units.ListAsync(ct);

    [HttpPost]
    [HasPermission(Permissions.Products.Manage)]
    public async Task<ActionResult<UnitDto>> Create(CreateUnitRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await units.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    public Task<UnitDto> Update(Guid id, UpdateUnitRequest request, CancellationToken ct) => units.UpdateAsync(id, request, ct);
}

[ApiController]
[Route("api/categories")]
public sealed class CategoriesController(CategoryService categories) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Products.View)]
    public Task<IReadOnlyList<CategoryDto>> List(CancellationToken ct) => categories.ListAsync(ct);

    [HttpPost]
    [HasPermission(Permissions.Products.Manage)]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await categories.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    public Task<CategoryDto> Update(Guid id, UpdateCategoryRequest request, CancellationToken ct) => categories.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await categories.DeleteAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Products.View)]
    public Task<PagedResult<ProductListItemDto>> List([FromQuery] ProductQuery query, CancellationToken ct) => products.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Products.View)]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    /// <summary>Barcode scan lookup (product or alternate-unit barcode).</summary>
    [HttpGet("by-barcode/{barcode}")]
    [HasPermission(Permissions.Products.View)]
    public Task<ProductDto> GetByBarcode(string barcode, CancellationToken ct) => products.GetByBarcodeAsync(barcode, ct);

    [HttpPost]
    [HasPermission(Permissions.Products.Manage)]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken ct)
    {
        var product = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    public Task<ProductDto> Update(Guid id, UpdateProductRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/image")]
    [HasPermission(Permissions.Products.Manage)]
    [RequestSizeLimit(3 * 1024 * 1024)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        await products.SetImageAsync(id, file.FileName, file.ContentType, buffer.ToArray(), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/image")]
    [HasPermission(Permissions.Products.View)]
    public async Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        var image = await products.GetImageAsync(id, ct);
        Response.Headers.CacheControl = "private, max-age=300";
        return File(image.Content, image.ContentType);
    }

    [HttpDelete("{id:guid}/image")]
    [HasPermission(Permissions.Products.Manage)]
    public async Task<IActionResult> RemoveImage(Guid id, CancellationToken ct)
    {
        await products.RemoveImageAsync(id, ct);
        return NoContent();
    }
}
