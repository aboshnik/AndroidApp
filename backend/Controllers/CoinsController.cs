using EmployeeApi.Services.Coins;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;

namespace EmployeeApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CoinsController : ControllerBase
{
    private readonly ICoinsService _coinsService;
    private readonly IConfiguration _configuration;

    public CoinsController(ICoinsService coinsService, IConfiguration configuration)
    {
        _coinsService = coinsService;
        _configuration = configuration;
    }

    [HttpGet("balance")]
    public async Task<ActionResult<CoinsBalanceResponse>> GetBalance([FromQuery] string login, CancellationToken cancellationToken)
    {
        var result = await _coinsService.GetBalanceAsync(login, cancellationToken);
        return Ok(new CoinsBalanceResponse(
            result.Success,
            result.Message,
            result.Success ? new CoinsBalanceItem(result.Login, result.Balance, result.NextPayoutDays) : null
        ));
    }

    [HttpPost("grant")]
    public async Task<ActionResult<CoinsBalanceResponse>> Grant([FromBody] CoinsMutationRequest request, CancellationToken cancellationToken)
    {
        if (!IsAdminAuthorized())
            return StatusCode(403, new CoinsBalanceResponse(false, "Доступ запрещен", null));
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new CoinsBalanceResponse(false, "Укажите login", null));

        var result = await _coinsService.AddCoinsAsync(request.Login, request.Amount, request.Reason, cancellationToken);
        return Ok(new CoinsBalanceResponse(
            result.Success,
            result.Message,
            result.Success ? new CoinsBalanceItem(result.Login, result.Balance, result.NextPayoutDays) : null
        ));
    }

    [HttpPost("spend")]
    public async Task<ActionResult<CoinsBalanceResponse>> Spend([FromBody] CoinsMutationRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new CoinsBalanceResponse(false, "Укажите login", null));

        var result = await _coinsService.SpendCoinsAsync(request.Login, request.Amount, request.Reason, cancellationToken);
        return Ok(new CoinsBalanceResponse(
            result.Success,
            result.Message,
            result.Success ? new CoinsBalanceItem(result.Login, result.Balance, result.NextPayoutDays) : null
        ));
    }

    [HttpGet("shop")]
    public async Task<ActionResult<CoinsShopResponse>> GetShop([FromQuery] string? login, CancellationToken cancellationToken)
    {
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new CoinsShopResponse(false, "Не настроено подключение к БД", new List<CoinsShopItem>()));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopTablesAsync(connection, cancellationToken);

            var normalizedLogin = login?.Trim() ?? "";
            var items = new List<CoinsShopItem>();
            const string sql = @"
                SELECT P.[Id], P.[Title], P.[Description], P.[Price], P.[Category], P.[ImageUrl], P.[Stock],
                       ISNULL(CI.[Quantity], 0) AS InCartQty
                FROM [App_CoinsShopProducts] P
                LEFT JOIN [App_CoinsCartItems] CI ON CI.[ProductId] = P.[Id] AND CI.[Login] = @Login
                WHERE ISNULL(P.[IsActive], 1) = 1
                ORDER BY P.[CreatedAt] DESC;";
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", normalizedLogin);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CoinsShopItem(
                    Id: Convert.ToInt32(reader.GetValue(0)).ToString(),
                    Title: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Price: reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                    Category: reader.IsDBNull(4) ? "Товары" : reader.GetString(4),
                    Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                    ImageUrl: reader.IsDBNull(5) ? null : reader.GetString(5),
                    Stock: reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6)),
                    InCartQty: reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7))
                ));
            }
            return Ok(new CoinsShopResponse(true, "OK", items));
        }
        catch (Exception ex)
        {
            return Ok(new CoinsShopResponse(false, $"Ошибка: {ex.Message}", new List<CoinsShopItem>()));
        }
    }

    [HttpPost("shop/products")]
    public async Task<ActionResult<CoinsShopResponse>> CreateShopProduct([FromBody] ShopProductCreateRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new CoinsShopResponse(false, "Укажите login", new List<CoinsShopItem>()));
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new CoinsShopResponse(false, "Укажите название товара", new List<CoinsShopItem>()));
        if (request.Price <= 0)
            return BadRequest(new CoinsShopResponse(false, "Цена должна быть больше 0", new List<CoinsShopItem>()));
        if (request.Stock < 0)
            return BadRequest(new CoinsShopResponse(false, "Остаток не может быть отрицательным", new List<CoinsShopItem>()));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new CoinsShopResponse(false, "Не настроено подключение к БД", new List<CoinsShopItem>()));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopTablesAsync(connection, cancellationToken);
            if (!await IsTechAdminByLoginAsync(connection, request.Login, cancellationToken))
                return StatusCode(403, new CoinsShopResponse(false, "Только техадмин может добавлять товары", new List<CoinsShopItem>()));

            const string ins = @"
                INSERT INTO [App_CoinsShopProducts]([Title],[Description],[Price],[Category],[ImageUrl],[Stock],[IsActive],[CreatedAt],[CreatedByLogin])
                VALUES(@Title,@Description,@Price,@Category,@ImageUrl,@Stock,1,GETUTCDATE(),@Login);";
            await using var cmd = new SqlCommand(ins, connection);
            cmd.Parameters.AddWithValue("@Title", request.Title.Trim());
            cmd.Parameters.AddWithValue("@Description", (object?)request.Description?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Price", request.Price);
            cmd.Parameters.AddWithValue("@Category", (object?)request.Category?.Trim() ?? "Товары");
            cmd.Parameters.AddWithValue("@ImageUrl", (object?)request.ImageUrl?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Stock", request.Stock);
            cmd.Parameters.AddWithValue("@Login", request.Login.Trim());
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return await GetShop(request.Login, cancellationToken);
        }
        catch (Exception ex)
        {
            return Ok(new CoinsShopResponse(false, $"Ошибка: {ex.Message}", new List<CoinsShopItem>()));
        }
    }

    [HttpPost("shop/products/image")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ShopImageUploadResponse>> UploadShopProductImage([FromQuery] string login, IFormFile? file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new ShopImageUploadResponse(false, "Укажите login", null));
        if (file == null || file.Length <= 0)
            return BadRequest(new ShopImageUploadResponse(false, "Выберите файл", null));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new ShopImageUploadResponse(false, "Не настроено подключение к БД", null));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            if (!await IsTechAdminByLoginAsync(connection, login, cancellationToken))
                return StatusCode(403, new ShopImageUploadResponse(false, "Только техадмин может загружать фото товара", null));

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? ".jpg";
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8) ext = ".jpg";
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            if (!allowed.Contains(ext))
                return Ok(new ShopImageUploadResponse(false, "Допустимы только изображения (jpg/png/webp/gif)", null));

            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "shop");
            Directory.CreateDirectory(uploadsRoot);
            var fileName = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadsRoot, fileName);
            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var url = $"{baseUrl}/uploads/shop/{Uri.EscapeDataString(fileName)}";
            return Ok(new ShopImageUploadResponse(true, "OK", url));
        }
        catch (Exception ex)
        {
            return Ok(new ShopImageUploadResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    [HttpPost("shop/cart/add")]
    public async Task<ActionResult<CoinsCartResponse>> AddToCart([FromBody] ShopCartAddRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new CoinsCartResponse(false, "Укажите login", new List<CoinsCartItem>(), 0));
        if (request.ProductId <= 0 || request.Quantity <= 0)
            return BadRequest(new CoinsCartResponse(false, "Некорректный товар или количество", new List<CoinsCartItem>(), 0));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new CoinsCartResponse(false, "Не настроено подключение к БД", new List<CoinsCartItem>(), 0));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopTablesAsync(connection, cancellationToken);

            const string getStockSql = @"SELECT TOP 1 ISNULL([Stock],0) FROM [App_CoinsShopProducts] WHERE [Id]=@Id AND ISNULL([IsActive],1)=1;";
            int stock;
            await using (var st = new SqlCommand(getStockSql, connection))
            {
                st.Parameters.AddWithValue("@Id", request.ProductId);
                var o = await st.ExecuteScalarAsync(cancellationToken);
                if (o == null || o == DBNull.Value) return Ok(new CoinsCartResponse(false, "Товар не найден", new List<CoinsCartItem>(), 0));
                stock = Convert.ToInt32(o);
            }

            const string getCurrentSql = @"SELECT TOP 1 ISNULL([Quantity],0) FROM [App_CoinsCartItems] WHERE [Login]=@Login AND [ProductId]=@ProductId;";
            var current = 0;
            await using (var cur = new SqlCommand(getCurrentSql, connection))
            {
                cur.Parameters.AddWithValue("@Login", request.Login.Trim());
                cur.Parameters.AddWithValue("@ProductId", request.ProductId);
                var o = await cur.ExecuteScalarAsync(cancellationToken);
                current = (o == null || o == DBNull.Value) ? 0 : Convert.ToInt32(o);
            }

            var next = current + request.Quantity;
            if (next > stock) return Ok(new CoinsCartResponse(false, "Недостаточно товара в наличии", new List<CoinsCartItem>(), 0));

            const string upsertSql = @"
                IF EXISTS(SELECT 1 FROM [App_CoinsCartItems] WHERE [Login]=@Login AND [ProductId]=@ProductId)
                    UPDATE [App_CoinsCartItems] SET [Quantity]=@Quantity, [UpdatedAt]=GETUTCDATE()
                    WHERE [Login]=@Login AND [ProductId]=@ProductId
                ELSE
                    INSERT INTO [App_CoinsCartItems]([Login],[ProductId],[Quantity],[UpdatedAt]) VALUES(@Login,@ProductId,@Quantity,GETUTCDATE());";
            await using var up = new SqlCommand(upsertSql, connection);
            up.Parameters.AddWithValue("@Login", request.Login.Trim());
            up.Parameters.AddWithValue("@ProductId", request.ProductId);
            up.Parameters.AddWithValue("@Quantity", next);
            await up.ExecuteNonQueryAsync(cancellationToken);

            return await GetCart(request.Login, cancellationToken);
        }
        catch (Exception ex)
        {
            return Ok(new CoinsCartResponse(false, $"Ошибка: {ex.Message}", new List<CoinsCartItem>(), 0));
        }
    }

    [HttpPost("shop/cart/set-quantity")]
    public async Task<ActionResult<CoinsCartResponse>> SetCartQuantity([FromBody] ShopCartSetQuantityRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new CoinsCartResponse(false, "Укажите login", new List<CoinsCartItem>(), 0));
        if (request.ProductId <= 0 || request.Quantity < 0)
            return BadRequest(new CoinsCartResponse(false, "Некорректный товар или количество", new List<CoinsCartItem>(), 0));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new CoinsCartResponse(false, "Не настроено подключение к БД", new List<CoinsCartItem>(), 0));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopTablesAsync(connection, cancellationToken);

            const string getStockSql = @"SELECT TOP 1 ISNULL([Stock],0) FROM [App_CoinsShopProducts] WHERE [Id]=@Id AND ISNULL([IsActive],1)=1;";
            int stock;
            await using (var st = new SqlCommand(getStockSql, connection))
            {
                st.Parameters.AddWithValue("@Id", request.ProductId);
                var o = await st.ExecuteScalarAsync(cancellationToken);
                if (o == null || o == DBNull.Value) return Ok(new CoinsCartResponse(false, "Товар не найден", new List<CoinsCartItem>(), 0));
                stock = Convert.ToInt32(o);
            }
            if (request.Quantity > stock)
                return Ok(new CoinsCartResponse(false, "Недостаточно товара в наличии", new List<CoinsCartItem>(), 0));

            if (request.Quantity == 0)
            {
                const string delSql = @"DELETE FROM [App_CoinsCartItems] WHERE [Login]=@Login AND [ProductId]=@ProductId;";
                await using var del = new SqlCommand(delSql, connection);
                del.Parameters.AddWithValue("@Login", request.Login.Trim());
                del.Parameters.AddWithValue("@ProductId", request.ProductId);
                await del.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                const string upsertSql = @"
                    IF EXISTS(SELECT 1 FROM [App_CoinsCartItems] WHERE [Login]=@Login AND [ProductId]=@ProductId)
                        UPDATE [App_CoinsCartItems] SET [Quantity]=@Quantity, [UpdatedAt]=GETUTCDATE()
                        WHERE [Login]=@Login AND [ProductId]=@ProductId
                    ELSE
                        INSERT INTO [App_CoinsCartItems]([Login],[ProductId],[Quantity],[UpdatedAt]) VALUES(@Login,@ProductId,@Quantity,GETUTCDATE());";
                await using var up = new SqlCommand(upsertSql, connection);
                up.Parameters.AddWithValue("@Login", request.Login.Trim());
                up.Parameters.AddWithValue("@ProductId", request.ProductId);
                up.Parameters.AddWithValue("@Quantity", request.Quantity);
                await up.ExecuteNonQueryAsync(cancellationToken);
            }

            return await GetCart(request.Login, cancellationToken);
        }
        catch (Exception ex)
        {
            return Ok(new CoinsCartResponse(false, $"Ошибка: {ex.Message}", new List<CoinsCartItem>(), 0));
        }
    }

    [HttpGet("shop/cart")]
    public async Task<ActionResult<CoinsCartResponse>> GetCart([FromQuery] string login, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new CoinsCartResponse(false, "Укажите login", new List<CoinsCartItem>(), 0));
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new CoinsCartResponse(false, "Не настроено подключение к БД", new List<CoinsCartItem>(), 0));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopTablesAsync(connection, cancellationToken);
            const string sql = @"
                SELECT CI.[ProductId], P.[Title], P.[ImageUrl], P.[Price], CI.[Quantity]
                FROM [App_CoinsCartItems] CI
                INNER JOIN [App_CoinsShopProducts] P ON P.[Id]=CI.[ProductId]
                WHERE CI.[Login]=@Login
                ORDER BY P.[Title];";
            var items = new List<CoinsCartItem>();
            var total = 0;
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", login.Trim());
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var productId = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                var title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var imageUrl = reader.IsDBNull(2) ? null : reader.GetString(2);
                var price = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                var qty = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));
                var line = price * qty;
                total += line;
                items.Add(new CoinsCartItem(productId, title, imageUrl, price, qty, line));
            }
            return Ok(new CoinsCartResponse(true, "OK", items, total));
        }
        catch (Exception ex)
        {
            return Ok(new CoinsCartResponse(false, $"Ошибка: {ex.Message}", new List<CoinsCartItem>(), 0));
        }
    }

    [HttpPost("shop/cart/checkout")]
    public async Task<ActionResult<ShopCheckoutResponse>> CheckoutCart([FromBody] ShopCheckoutRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Login))
            return BadRequest(new ShopCheckoutResponse(false, "Укажите login", 0, 0, 0));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new ShopCheckoutResponse(false, "Не настроено подключение к БД", 0, 0, 0));

        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopTablesAsync(connection, cancellationToken);
            await EnsureShopOrdersTablesAsync(connection, cancellationToken);
            await EnsureCoinsTablesAsync(connection, cancellationToken);

            var login = request.Login.Trim();
            var ownerLogin = await ResolveOwnerLoginAsync(connection, login, cancellationToken);

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var cartRows = new List<(int ProductId, int Price, int Qty, int Stock)>();
            const string cartSql = @"
                SELECT CI.[ProductId], ISNULL(P.[Price],0), ISNULL(CI.[Quantity],0), ISNULL(P.[Stock],0)
                FROM [App_CoinsCartItems] CI WITH (UPDLOCK, ROWLOCK)
                INNER JOIN [App_CoinsShopProducts] P WITH (UPDLOCK, ROWLOCK) ON P.[Id]=CI.[ProductId]
                WHERE CI.[Login]=@Login
                ;";
            await using (var cmd = new SqlCommand(cartSql, connection, tx))
            {
                cmd.Parameters.AddWithValue("@Login", ownerLogin);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    cartRows.Add((
                        ProductId: reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        Price: reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                        Qty: reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                        Stock: reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3))
                    ));
                }
            }
            if (cartRows.Count == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return Ok(new ShopCheckoutResponse(false, "Корзина пуста", 0, 0, 0));
            }

            var total = 0;
            foreach (var row in cartRows)
            {
                if (row.Qty <= 0 || row.ProductId <= 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Ok(new ShopCheckoutResponse(false, "Некорректные данные корзины", 0, 0, 0));
                }
                if (row.Stock < row.Qty)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Ok(new ShopCheckoutResponse(false, "Недостаточно товара в наличии", 0, 0, 0));
                }
                total += row.Price * row.Qty;
            }
            if (total <= 0)
            {
                await tx.RollbackAsync(cancellationToken);
                return Ok(new ShopCheckoutResponse(false, "Сумма заказа некорректна", 0, 0, 0));
            }

            var currentBalance = await GetBalanceInternalAsync(connection, ownerLogin, cancellationToken, tx);
            if (currentBalance < total)
            {
                await tx.RollbackAsync(cancellationToken);
                return Ok(new ShopCheckoutResponse(false, "Недостаточно коинов", 0, currentBalance, total));
            }

            foreach (var row in cartRows)
            {
                const string stockSql = @"UPDATE [App_CoinsShopProducts] SET [Stock]=[Stock]-@Qty WHERE [Id]=@Id;";
                await using var st = new SqlCommand(stockSql, connection, tx);
                st.Parameters.AddWithValue("@Qty", row.Qty);
                st.Parameters.AddWithValue("@Id", row.ProductId);
                await st.ExecuteNonQueryAsync(cancellationToken);
            }

            var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            const string orderSql = @"
                INSERT INTO [App_CoinsShopOrders]([OrderNumber],[Login],[ItemsCount],[TotalAmount],[CreatedAt])
                VALUES(@OrderNumber,@Login,@ItemsCount,@TotalAmount,GETUTCDATE());
                SELECT SCOPE_IDENTITY();";
            int orderId;
            await using (var ocmd = new SqlCommand(orderSql, connection, tx))
            {
                ocmd.Parameters.AddWithValue("@OrderNumber", orderNumber);
                ocmd.Parameters.AddWithValue("@Login", ownerLogin);
                ocmd.Parameters.AddWithValue("@ItemsCount", cartRows.Sum(x => x.Qty));
                ocmd.Parameters.AddWithValue("@TotalAmount", total);
                var o = await ocmd.ExecuteScalarAsync(cancellationToken);
                orderId = Convert.ToInt32(o);
            }

            const string orderItemSql = @"
                INSERT INTO [App_CoinsShopOrderItems]([OrderId],[ProductId],[Price],[Quantity],[LineTotal])
                VALUES(@OrderId,@ProductId,@Price,@Quantity,@LineTotal);";
            foreach (var row in cartRows)
            {
                await using var icmd = new SqlCommand(orderItemSql, connection, tx);
                icmd.Parameters.AddWithValue("@OrderId", orderId);
                icmd.Parameters.AddWithValue("@ProductId", row.ProductId);
                icmd.Parameters.AddWithValue("@Price", row.Price);
                icmd.Parameters.AddWithValue("@Quantity", row.Qty);
                icmd.Parameters.AddWithValue("@LineTotal", row.Price * row.Qty);
                await icmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string clearSql = @"DELETE FROM [App_CoinsCartItems] WHERE [Login]=@Login;";
            await using (var clr = new SqlCommand(clearSql, connection, tx))
            {
                clr.Parameters.AddWithValue("@Login", ownerLogin);
                await clr.ExecuteNonQueryAsync(cancellationToken);
            }

            var nextBalance = currentBalance - total;
            await SetBalanceInternalAsync(connection, ownerLogin, nextBalance, cancellationToken, tx);
            await InsertTransferInternalAsync(connection, ownerLogin, -total, nextBalance, "spend", "Покупка в магазине", "user", cancellationToken, tx);
            await tx.CommitAsync(cancellationToken);

            return Ok(new ShopCheckoutResponse(true, "Заказ оформлен", cartRows.Sum(x => x.Qty), nextBalance, total));
        }
        catch (Exception ex)
        {
            return Ok(new ShopCheckoutResponse(false, $"Ошибка: {ex.Message}", 0, 0, 0));
        }
    }

    [HttpGet("shop/orders")]
    public async Task<ActionResult<ShopOrdersResponse>> GetOrders([FromQuery] string login, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new ShopOrdersResponse(false, "Укажите login", new List<ShopOrderItem>()));
        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new ShopOrdersResponse(false, "Не настроено подключение к БД", new List<ShopOrderItem>()));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopOrdersTablesAsync(connection, cancellationToken);
            var ownerLogin = await ResolveOwnerLoginAsync(connection, login.Trim(), cancellationToken);

            const string sql = @"
                SELECT TOP 30 [OrderNumber], [ItemsCount], [TotalAmount], [CreatedAt]
                FROM [App_CoinsShopOrders]
                WHERE [Login]=@Login
                ORDER BY [CreatedAt] DESC;";
            var list = new List<ShopOrderItem>();
            await using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Login", ownerLogin);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ShopOrderItem(
                    OrderNumber: reader.IsDBNull(0) ? "" : reader.GetString(0),
                    ItemsCount: reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                    TotalAmount: reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                    CreatedAt: reader.IsDBNull(3) ? DateTime.UtcNow : reader.GetDateTime(3)
                ));
            }
            return Ok(new ShopOrdersResponse(true, "OK", list));
        }
        catch (Exception ex)
        {
            return Ok(new ShopOrdersResponse(false, $"Ошибка: {ex.Message}", new List<ShopOrderItem>()));
        }
    }

    [HttpGet("shop/order-details")]
    public async Task<ActionResult<ShopOrderDetailsResponse>> GetOrderDetails([FromQuery] string login, [FromQuery] string orderNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login))
            return BadRequest(new ShopOrderDetailsResponse(false, "Укажите login", null));
        if (string.IsNullOrWhiteSpace(orderNumber))
            return BadRequest(new ShopOrderDetailsResponse(false, "Укажите номер заказа", null));

        var cs = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            return StatusCode(500, new ShopOrderDetailsResponse(false, "Не настроено подключение к БД", null));
        try
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync(cancellationToken);
            await EnsureShopOrdersTablesAsync(connection, cancellationToken);
            var ownerLogin = await ResolveOwnerLoginAsync(connection, login.Trim(), cancellationToken);

            const string headerSql = @"
                SELECT TOP 1 O.[Id], O.[OrderNumber], O.[ItemsCount], O.[TotalAmount], O.[CreatedAt]
                FROM [App_CoinsShopOrders] O
                WHERE O.[Login]=@Login AND O.[OrderNumber]=@OrderNumber;";
            int orderId;
            ShopOrderItem? header;
            await using (var hcmd = new SqlCommand(headerSql, connection))
            {
                hcmd.Parameters.AddWithValue("@Login", ownerLogin);
                hcmd.Parameters.AddWithValue("@OrderNumber", orderNumber.Trim());
                await using var reader = await hcmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    return Ok(new ShopOrderDetailsResponse(false, "Заказ не найден", null));

                orderId = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0));
                header = new ShopOrderItem(
                    OrderNumber: reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ItemsCount: reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                    TotalAmount: reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                    CreatedAt: reader.IsDBNull(4) ? DateTime.UtcNow : reader.GetDateTime(4)
                );
            }

            const string itemsSql = @"
                SELECT OI.[ProductId], ISNULL(P.[Title], CONCAT(N'Товар #', CONVERT(nvarchar(20), OI.[ProductId]))), ISNULL(P.[ImageUrl], NULL),
                       OI.[Price], OI.[Quantity], OI.[LineTotal]
                FROM [App_CoinsShopOrderItems] OI
                LEFT JOIN [App_CoinsShopProducts] P ON P.[Id] = OI.[ProductId]
                WHERE OI.[OrderId] = @OrderId
                ORDER BY OI.[Id] ASC;";
            var detailItems = new List<ShopOrderDetailItem>();
            await using (var icmd = new SqlCommand(itemsSql, connection))
            {
                icmd.Parameters.AddWithValue("@OrderId", orderId);
                await using var reader = await icmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    detailItems.Add(new ShopOrderDetailItem(
                        ProductId: reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
                        Title: reader.IsDBNull(1) ? "" : reader.GetString(1),
                        ImageUrl: reader.IsDBNull(2) ? null : reader.GetString(2),
                        Price: reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                        Quantity: reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                        LineTotal: reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5))
                    ));
                }
            }

            var details = new ShopOrderDetailsItem(
                OrderNumber: header!.OrderNumber,
                ItemsCount: header.ItemsCount,
                TotalAmount: header.TotalAmount,
                CreatedAt: header.CreatedAt,
                Items: detailItems
            );
            return Ok(new ShopOrderDetailsResponse(true, "OK", details));
        }
        catch (Exception ex)
        {
            return Ok(new ShopOrderDetailsResponse(false, $"Ошибка: {ex.Message}", null));
        }
    }

    private static async Task EnsureCoinsTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_Balance')
            CREATE TABLE [App_Balance] (
                [Login] NVARCHAR(100) NOT NULL PRIMARY KEY,
                [Balance] INT NOT NULL CONSTRAINT [DF_App_Balance_Balance] DEFAULT 0,
                [UpdatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_Balance_UpdatedAt] DEFAULT GETUTCDATE()
            );

            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_CoinsTransfers')
            CREATE TABLE [App_CoinsTransfers] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Login] NVARCHAR(100) NOT NULL,
                [Delta] INT NOT NULL,
                [BalanceAfter] INT NOT NULL,
                [Type] NVARCHAR(50) NOT NULL,
                [Reason] NVARCHAR(500) NULL,
                [Actor] NVARCHAR(100) NULL,
                [Source] NVARCHAR(100) NULL,
                [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_App_CoinsTransfers_CreatedAt] DEFAULT GETUTCDATE()
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetBalanceInternalAsync(SqlConnection connection, string login, CancellationToken cancellationToken, SqlTransaction tx)
    {
        const string sql = @"SELECT TOP 1 ISNULL([Balance],0) FROM [App_Balance] WHERE [Login]=@Login;";
        await using var cmd = new SqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Login", login);
        var o = await cmd.ExecuteScalarAsync(cancellationToken);
        return (o == null || o == DBNull.Value) ? 0 : Convert.ToInt32(o);
    }

    private static async Task SetBalanceInternalAsync(SqlConnection connection, string login, int balance, CancellationToken cancellationToken, SqlTransaction tx)
    {
        const string sql = @"
            IF EXISTS(SELECT 1 FROM [App_Balance] WHERE [Login]=@Login)
                UPDATE [App_Balance] SET [Balance]=@Balance, [UpdatedAt]=GETUTCDATE() WHERE [Login]=@Login
            ELSE
                INSERT INTO [App_Balance]([Login],[Balance],[UpdatedAt]) VALUES(@Login,@Balance,GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@Balance", balance);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertTransferInternalAsync(SqlConnection connection, string login, int delta, int balanceAfter, string type, string? reason, string? actor, CancellationToken cancellationToken, SqlTransaction tx)
    {
        const string sql = @"
            INSERT INTO [App_CoinsTransfers]([Login],[Delta],[BalanceAfter],[Type],[Reason],[Actor],[Source],[CreatedAt])
            VALUES(@Login,@Delta,@BalanceAfter,@Type,@Reason,@Actor,NULL,GETUTCDATE());";
        await using var cmd = new SqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Login", login);
        cmd.Parameters.AddWithValue("@Delta", delta);
        cmd.Parameters.AddWithValue("@BalanceAfter", balanceAfter);
        cmd.Parameters.AddWithValue("@Type", type);
        cmd.Parameters.AddWithValue("@Reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Actor", (object?)actor ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> ResolveOwnerLoginAsync(SqlConnection connection, string loginOrEmployeeId, CancellationToken cancellationToken)
    {
        var key = loginOrEmployeeId.Trim();
        if (!key.All(char.IsDigit)) return key;
        const string sql = @"
            SELECT TOP 1 COALESCE(TRY_CONVERT(nvarchar(200), [Логин]), '')
            FROM [Lexema_Кадры_ЛичнаяКарточка]
            WHERE TRY_CONVERT(nvarchar(50), [ТабельныйНомер]) = @EmployeeId;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@EmployeeId", key);
        var o = await cmd.ExecuteScalarAsync(cancellationToken);
        var resolved = (o == null || o == DBNull.Value) ? "" : Convert.ToString(o)?.Trim();
        return string.IsNullOrWhiteSpace(resolved) ? key : resolved!;
    }

    private static async Task EnsureShopTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_CoinsShopProducts')
            CREATE TABLE [App_CoinsShopProducts] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [Title] NVARCHAR(200) NOT NULL,
                [Description] NVARCHAR(2000) NULL,
                [Price] INT NOT NULL,
                [Category] NVARCHAR(120) NULL,
                [ImageUrl] NVARCHAR(1000) NULL,
                [Stock] INT NOT NULL DEFAULT 0,
                [IsActive] BIT NOT NULL DEFAULT 1,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                [CreatedByLogin] NVARCHAR(100) NULL
            );
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_CoinsCartItems')
            CREATE TABLE [App_CoinsCartItems] (
                [Login] NVARCHAR(100) NOT NULL,
                [ProductId] INT NOT NULL,
                [Quantity] INT NOT NULL DEFAULT 1,
                [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT [PK_App_CoinsCartItems] PRIMARY KEY ([Login], [ProductId])
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureShopOrdersTablesAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_CoinsShopOrders')
            CREATE TABLE [App_CoinsShopOrders] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [OrderNumber] NVARCHAR(80) NOT NULL,
                [Login] NVARCHAR(100) NOT NULL,
                [ItemsCount] INT NOT NULL,
                [TotalAmount] INT NOT NULL,
                [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE()
            );
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'App_CoinsShopOrderItems')
            CREATE TABLE [App_CoinsShopOrderItems] (
                [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [OrderId] INT NOT NULL,
                [ProductId] INT NOT NULL,
                [Price] INT NOT NULL,
                [Quantity] INT NOT NULL,
                [LineTotal] INT NOT NULL
            );";
        await using var cmd = new SqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsTechAdminByLoginAsync(SqlConnection connection, string login, CancellationToken cancellationToken)
    {
        var key = login.Trim();
        if (string.IsNullOrWhiteSpace(key)) return false;
        const string sql = @"SELECT TOP 1 ISNULL([CanTechAdmin],0) FROM [App_UserPermissions] WHERE [Login]=@Login;";
        await using var cmd = new SqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Login", key);
        var o = await cmd.ExecuteScalarAsync(cancellationToken);
        return o != null && o != DBNull.Value && Convert.ToInt32(o) == 1;
    }

    private bool IsAdminAuthorized()
    {
        var expected = _configuration["Admin:Key"]?.Trim();
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var provided = Request.Headers["X-Admin-Key"].ToString().Trim();
        return string.Equals(expected, provided, StringComparison.Ordinal);
    }
}

public record CoinsBalanceItem(string Login, int Balance, int NextPayoutDays);
public record CoinsBalanceResponse(bool Success, string Message, CoinsBalanceItem? Balance);
public record CoinsMutationRequest(string Login, int Amount, string? Reason = null);
public record CoinsShopItem(string Id, string Title, int Price, string Category, string? Description = null, string? ImageUrl = null, int Stock = 0, int InCartQty = 0);
public record CoinsShopResponse(bool Success, string Message, List<CoinsShopItem> Items);
public record ShopProductCreateRequest(string Login, string Title, string? Description, int Price, int Stock, string? ImageUrl = null, string? Category = null);
public record ShopCartAddRequest(string Login, int ProductId, int Quantity = 1);
public record ShopCartSetQuantityRequest(string Login, int ProductId, int Quantity);
public record CoinsCartItem(int ProductId, string Title, string? ImageUrl, int Price, int Quantity, int LineTotal);
public record CoinsCartResponse(bool Success, string Message, List<CoinsCartItem> Items, int TotalAmount);
public record ShopImageUploadResponse(bool Success, string Message, string? ImageUrl);
public record ShopCheckoutRequest(string Login);
public record ShopCheckoutResponse(bool Success, string Message, int ItemsCount, int BalanceAfter, int TotalSpent);
public record ShopOrderItem(string OrderNumber, int ItemsCount, int TotalAmount, DateTime CreatedAt);
public record ShopOrdersResponse(bool Success, string Message, List<ShopOrderItem> Items);
public record ShopOrderDetailItem(int ProductId, string Title, string? ImageUrl, int Price, int Quantity, int LineTotal);
public record ShopOrderDetailsItem(string OrderNumber, int ItemsCount, int TotalAmount, DateTime CreatedAt, List<ShopOrderDetailItem> Items);
public record ShopOrderDetailsResponse(bool Success, string Message, ShopOrderDetailsItem? Order);

