using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Tracker.Models;

namespace Tracker.Services;

/// <summary>
/// Local persistence for tracked products, backed by a SQLite database (via Microsoft.Data.Sqlite).
///
/// The database file lives next to the rest of the app data (<c>%LocalAppData%\Tracker\tracker.db</c>) and is
/// created on first run: <see cref="Initialize"/> runs idempotent <c>CREATE TABLE IF NOT EXISTS</c> statements,
/// so it works for both a fresh install (unpackaged app) and later launches.
///
/// A connection is opened per operation from the connection string (SQLite pools them), which keeps the service
/// safe to call from the background price scheduler as well as the UI thread. Prices are stored as invariant
/// TEXT (exact decimals, no float rounding) and timestamps as round-trip ISO-8601 UTC.
/// </summary>
public sealed class ProductDatabaseService
{
    #region Attributes
    private readonly string _dbPath;
    private readonly string _connectionString;
    #endregion

    #region Constructor
    public ProductDatabaseService()
    {
        _dbPath = Path.Combine(PersistAndRestoreService.SettingsFolderPath, "tracker.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            ForeignKeys = true
        }.ToString();
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Ensures the database file and schema exist. Safe to call on every startup: creates the data folder and the
    /// tables only if they are missing (first run), otherwise does nothing.
    /// </summary>
    public void Initialize()
    {
        Directory.CreateDirectory(PersistAndRestoreService.SettingsFolderPath);

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Products (
                Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                Name      TEXT    NOT NULL DEFAULT '',
                ImageUrl  TEXT    NULL,
                CreatedAt TEXT    NOT NULL,
                Purchased INTEGER NOT NULL DEFAULT 0,
                PurchasePrice TEXT NULL,
                PurchasedAt TEXT NULL,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                AlertPrice TEXT NULL,
                Deleted    INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS ProductStores (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductId    INTEGER NOT NULL,
                Url          TEXT    NOT NULL,
                Label        TEXT    NOT NULL DEFAULT '',
                Currency     TEXT    NULL,
                CurrentPrice TEXT    NULL,
                LastChecked  TEXT    NULL,
                IsPrime      INTEGER NOT NULL DEFAULT 0,
                IsAvailable  INTEGER NOT NULL DEFAULT 1,
                HasPromo     INTEGER NOT NULL DEFAULT 0,
                IsPreorder   INTEGER NOT NULL DEFAULT 0,
                PriceSelector TEXT   NULL,
                ShippingCost TEXT    NULL,
                Deleted      INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS PriceHistory (
                Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductStoreId INTEGER NOT NULL,
                Price          TEXT    NOT NULL,
                Timestamp      TEXT    NOT NULL,
                FOREIGN KEY (ProductStoreId) REFERENCES ProductStores(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AppMeta (
                Key   TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ProductStores_ProductId ON ProductStores(ProductId);
            CREATE INDEX IF NOT EXISTS IX_PriceHistory_ProductStoreId ON PriceHistory(ProductStoreId);
        ";
        command.ExecuteNonQuery();

        // Migraciones para bases de datos creadas antes de añadir estas columnas.
        EnsureColumn(connection, "ProductStores", "IsPrime", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "ProductStores", "IsAvailable", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "ProductStores", "HasPromo", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "ProductStores", "IsPreorder", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "ProductStores", "PriceSelector", "TEXT NULL");
        EnsureColumn(connection, "ProductStores", "ShippingCost", "TEXT NULL");
        EnsureColumn(connection, "ProductStores", "Deleted", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Products", "Purchased", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Products", "PurchasePrice", "TEXT NULL");
        EnsureColumn(connection, "Products", "PurchasedAt", "TEXT NULL");
        EnsureColumn(connection, "Products", "IsFavorite", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "Products", "AlertPrice", "TEXT NULL");
        EnsureColumn(connection, "Products", "Deleted", "INTEGER NOT NULL DEFAULT 0");
    }

    /// <summary>Añade una columna a una tabla si aún no existe (migración idempotente).</summary>
    private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
    {
        using (SqliteCommand check = connection.CreateCommand())
        {
            check.CommandText = $"PRAGMA table_info({table});";
            using SqliteDataReader reader = check.ExecuteReader();
            while (reader.Read())
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
        }

        using SqliteCommand alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    /// <summary>
    /// Loads every persisted product (with its stores and price history) into <paramref name="target"/>, in the
    /// order they were created. Clears the set first, so it can be used for a full refresh.
    /// </summary>
    public void LoadInto(ProductSet target)
    {
        target.Products.Clear();

        using SqliteConnection connection = OpenConnection();

        var productsById = new Dictionary<long, Product>();
        var storesById = new Dictionary<long, ProductStore>();

        // Products
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name, ImageUrl, IsFavorite, AlertPrice, Purchased, PurchasePrice, PurchasedAt FROM Products WHERE Deleted = 0 ORDER BY Id;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                var product = new Product
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    ImageUrl = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsFavorite = !reader.IsDBNull(3) && reader.GetInt64(3) != 0,
                    AlertPrice = reader.IsDBNull(4) ? null : ParsePrice(reader.GetString(4)),
                    IsPurchased = !reader.IsDBNull(5) && reader.GetInt64(5) != 0,
                    PurchasePrice = reader.IsDBNull(6) ? null : ParsePrice(reader.GetString(6)),
                    PurchasedAt = reader.IsDBNull(7) ? null : ParseTimestamp(reader.GetString(7))
                };
                productsById[product.Id] = product;
                target.Products.Add(product);
            }
        }

        // Stores
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ProductId, Url, Label, Currency, CurrentPrice, LastChecked, IsPrime, IsAvailable, HasPromo, PriceSelector, IsPreorder, ShippingCost FROM ProductStores WHERE Deleted = 0 ORDER BY Id;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long productId = reader.GetInt64(1);
                if (!productsById.TryGetValue(productId, out Product? product))
                    continue;

                var store = new ProductStore
                {
                    Id = reader.GetInt64(0),
                    Url = reader.GetString(2),
                    Label = reader.GetString(3),
                    Currency = reader.IsDBNull(4) ? null : reader.GetString(4),
                    CurrentPrice = reader.IsDBNull(5) ? null : ParsePrice(reader.GetString(5)),
                    LastChecked = reader.IsDBNull(6) ? null : ParseTimestamp(reader.GetString(6)),
                    IsPrime = !reader.IsDBNull(7) && reader.GetInt64(7) != 0,
                    IsAvailable = reader.IsDBNull(8) || reader.GetInt64(8) != 0,
                    HasPromo = !reader.IsDBNull(9) && reader.GetInt64(9) != 0,
                    PriceSelector = reader.IsDBNull(10) ? null : reader.GetString(10),
                    IsPreorder = !reader.IsDBNull(11) && reader.GetInt64(11) != 0,
                    ShippingCost = reader.IsDBNull(12) ? null : ParsePrice(reader.GetString(12))
                };
                storesById[store.Id] = store;
                product.Stores.Add(store);
            }
        }

        // Price history (joined to know the store label of each point)
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = @"
                SELECT ps.ProductId, ps.Label, ph.Price, ph.Timestamp, ph.ProductStoreId
                FROM PriceHistory ph
                JOIN ProductStores ps ON ps.Id = ph.ProductStoreId
                WHERE ps.Deleted = 0
                ORDER BY ph.Timestamp;";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long productId = reader.GetInt64(0);
                if (!productsById.TryGetValue(productId, out Product? product))
                    continue;

                product.PriceHistory.Add(new PricePoint(
                    ParseTimestamp(reader.GetString(3)),
                    ParsePrice(reader.GetString(2)),
                    reader.GetString(1),
                    reader.GetInt64(4)));
            }
        }
    }

    /// <summary>
    /// Inserts a new product and its stores, assigning the generated <see cref="Product.Id"/> and
    /// <see cref="ProductStore.Id"/> back onto the in-memory objects. Does not insert price history (a new product
    /// has none yet; readings are added later via <see cref="SavePriceReading"/>).
    /// </summary>
    public void InsertProduct(Product product)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO Products (Name, ImageUrl, CreatedAt) VALUES ($name, $imageUrl, $createdAt);";
            command.Parameters.AddWithValue("$name", product.Name);
            command.Parameters.AddWithValue("$imageUrl", (object?)product.ImageUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", FormatTimestamp(DateTime.UtcNow));
            command.ExecuteNonQuery();
            product.Id = LastInsertRowId(connection, transaction);
        }

        foreach (ProductStore store in product.Stores)
            InsertStoreCore(connection, transaction, product.Id, store);

        transaction.Commit();
    }

    /// <summary>Adds a new store to an already-persisted product and assigns its generated id.</summary>
    public void InsertStore(Product product, ProductStore store)
    {
        using SqliteConnection connection = OpenConnection();
        InsertStoreCore(connection, null, product.Id, store);
    }

    /// <summary>
    /// Persists a fresh price reading for a store: updates its current price/timestamp and appends a history row.
    /// This is what the price scheduler calls after fetching a new price. Mirrors <see cref="Product.RecordPrice"/>
    /// (which mutates the in-memory model); call both to keep memory and disk in sync.
    /// </summary>
    public void SavePriceReading(ProductStore store, decimal price, DateTime timestampUtc)
    {
        // Guarda: un store sin persistir (Id 0) no existe en ProductStores, así que insertar su histórico violaría la
        // clave foránea (y tumbaría el refresco). Se omite en vez de reventar; indica un fallo previo al persistir el store.
        if (store.Id <= 0)
        {
            ExceptionService.LogToFile(null, $"Skipped saving a price reading for a non-persisted store (Id=0) '{store.Label}'.");
            return;
        }

        using SqliteConnection connection = OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE ProductStores SET CurrentPrice = $price, LastChecked = $checked, Currency = $currency, IsPrime = $prime, IsAvailable = $available, HasPromo = $promo, IsPreorder = $preorder, ShippingCost = $shipping WHERE Id = $id;";
            command.Parameters.AddWithValue("$price", FormatPrice(price));
            command.Parameters.AddWithValue("$checked", FormatTimestamp(timestampUtc));
            command.Parameters.AddWithValue("$currency", (object?)store.Currency ?? DBNull.Value);
            command.Parameters.AddWithValue("$prime", store.IsPrime ? 1 : 0);
            command.Parameters.AddWithValue("$available", store.IsAvailable ? 1 : 0);
            command.Parameters.AddWithValue("$promo", store.HasPromo ? 1 : 0);
            command.Parameters.AddWithValue("$preorder", store.IsPreorder ? 1 : 0);
            command.Parameters.AddWithValue("$shipping", store.ShippingCost is decimal ship ? FormatPrice(ship) : (object)DBNull.Value);
            command.Parameters.AddWithValue("$id", store.Id);
            command.ExecuteNonQuery();
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO PriceHistory (ProductStoreId, Price, Timestamp) VALUES ($storeId, $price, $timestamp);";
            command.Parameters.AddWithValue("$storeId", store.Id);
            command.Parameters.AddWithValue("$price", FormatPrice(price));
            command.Parameters.AddWithValue("$timestamp", FormatTimestamp(timestampUtc));
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// Persists a store's status flags (Prime / availability / promo / currency / last-checked) WITHOUT recording a
    /// price reading. Used when a refresh pass read the page but there is no valid price to record (e.g. the product
    /// is currently unavailable), so the availability/promo state is still kept up to date on disk.
    /// </summary>
    public void UpdateStoreStatus(ProductStore store, DateTime timestampUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE ProductStores SET LastChecked = $checked, Currency = $currency, IsPrime = $prime, IsAvailable = $available, HasPromo = $promo, IsPreorder = $preorder, ShippingCost = $shipping WHERE Id = $id;";
        command.Parameters.AddWithValue("$checked", FormatTimestamp(timestampUtc));
        command.Parameters.AddWithValue("$currency", (object?)store.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue("$prime", store.IsPrime ? 1 : 0);
        command.Parameters.AddWithValue("$available", store.IsAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$promo", store.HasPromo ? 1 : 0);
        command.Parameters.AddWithValue("$preorder", store.IsPreorder ? 1 : 0);
        command.Parameters.AddWithValue("$shipping", store.ShippingCost is decimal ship ? FormatPrice(ship) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", store.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Updates the persisted name/image of a product (e.g. after parsing its page).</summary>
    public void UpdateProductInfo(Product product)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET Name = $name, ImageUrl = $imageUrl WHERE Id = $id;";
        command.Parameters.AddWithValue("$name", product.Name);
        command.Parameters.AddWithValue("$imageUrl", (object?)product.ImageUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", product.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Persists the favourite flag of a product.</summary>
    public void SetFavorite(Product product, bool isFavorite)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET IsFavorite = $fav WHERE Id = $id;";
        command.Parameters.AddWithValue("$fav", isFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$id", product.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Persists the user-picked price selector of a store (null clears it).</summary>
    public void SetPriceSelector(ProductStore store, string? selector)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE ProductStores SET PriceSelector = $selector WHERE Id = $id;";
        command.Parameters.AddWithValue("$selector", (object?)selector ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", store.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Persists the alert price of a product (null clears it).</summary>
    public void SetAlertPrice(Product product, decimal? alertPrice)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET AlertPrice = $price WHERE Id = $id;";
        command.Parameters.AddWithValue("$price", alertPrice is decimal value ? FormatPrice(value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", product.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Establece (o quita) el flag de comprado de un producto, guardando el precio de compra al marcarlo (y borrándolo
    /// al revertir). El producto sigue en la lista; solo cambia su tratamiento (título tachado, sin refrescar precios).
    /// </summary>
    public void SetPurchased(Product product, bool purchased, decimal? purchasePrice, DateTime? purchasedAtUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET Purchased = $purchased, PurchasePrice = $price, PurchasedAt = $at WHERE Id = $id;";
        command.Parameters.AddWithValue("$purchased", purchased ? 1 : 0);
        command.Parameters.AddWithValue("$price", purchased && purchasePrice is decimal p ? FormatPrice(p) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$at", purchased && purchasedAtUtc is DateTime at ? FormatTimestamp(at) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$id", product.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Marca (o desmarca) un producto como BORRADO sin eliminar su fila, sus tiendas ni su histórico: así el borrado
    /// se puede DESHACER desde el log. Los productos con <c>Deleted = 1</c> se excluyen de la carga
    /// (<see cref="LoadInto"/>), y con ellos sus tiendas y su histórico, que cuelgan del producto.
    /// </summary>
    public void SetProductDeleted(Product product, bool deleted)
    {
        if (product is null || product.Id <= 0)
            return;

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET Deleted = $deleted WHERE Id = $id;";
        command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
        command.Parameters.AddWithValue("$id", product.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Marca (o desmarca) una tienda como BORRADA sin eliminar la fila ni su histórico: así el borrado del enlace se
    /// puede DESHACER desde el log. Las tiendas con <c>Deleted = 1</c> se excluyen de la carga (<see cref="LoadInto"/>).
    /// </summary>
    public void SetStoreDeleted(ProductStore store, bool deleted)
    {
        if (store is null || store.Id <= 0)
            return;

        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE ProductStores SET Deleted = $deleted WHERE Id = $id;";
        command.Parameters.AddWithValue("$deleted", deleted ? 1 : 0);
        command.Parameters.AddWithValue("$id", store.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>Clave en <c>AppMeta</c> del momento de la última actualización COMPLETA de precios.</summary>
    private const string MetaLastFullRefresh = "LastFullRefreshUtc";

    /// <summary>
    /// Momento (UTC) de la última pasada COMPLETA de precios (periódica o "refrescar todo" manual), persistido en
    /// <c>AppMeta</c>, o null si aún no se ha hecho ninguna. Ancla la cuenta atrás del footer para que sobreviva al
    /// cierre de la app (a diferencia de las lecturas por tienda, no lo mueven los refrescos sueltos ni el catch-up).
    /// </summary>
    public DateTime? GetLastFullRefreshUtc()
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppMeta WHERE Key = $key;";
        command.Parameters.AddWithValue("$key", MetaLastFullRefresh);
        object? value = command.ExecuteScalar();
        return value is string raw && !string.IsNullOrWhiteSpace(raw) ? ParseTimestamp(raw) : null;
    }

    /// <summary>Persiste (upsert) el momento (UTC) de la última pasada COMPLETA de precios en <c>AppMeta</c>.</summary>
    public void SetLastFullRefreshUtc(DateTime timestampUtc)
    {
        using SqliteConnection connection = OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppMeta (Key, Value) VALUES ($key, $value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
        command.Parameters.AddWithValue("$key", MetaLastFullRefresh);
        command.Parameters.AddWithValue("$value", FormatTimestamp(timestampUtc));
        command.ExecuteNonQuery();
    }
    #endregion

    #region Methods (private)
    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // WAL + espera ante bloqueos: la app ahora escribe desde varios flujos (scheduler + altas + refrescos por
        // producto). WAL permite lectores y un escritor sin bloquearse; busy_timeout hace que un escritor espere en
        // vez de fallar con "database is locked". Barato y idempotente por conexión.
        using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();

        return connection;
    }

    private static void InsertStoreCore(SqliteConnection connection, SqliteTransaction? transaction, long productId, ProductStore store)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO ProductStores (ProductId, Url, Label, Currency, CurrentPrice, LastChecked, IsPrime, IsAvailable, HasPromo, IsPreorder, PriceSelector)
            VALUES ($productId, $url, $label, $currency, $price, $checked, $prime, $available, $promo, $preorder, $selector);";
        command.Parameters.AddWithValue("$productId", productId);
        command.Parameters.AddWithValue("$url", store.Url);
        command.Parameters.AddWithValue("$label", store.Label);
        command.Parameters.AddWithValue("$currency", (object?)store.Currency ?? DBNull.Value);
        command.Parameters.AddWithValue("$price", store.CurrentPrice is decimal p ? FormatPrice(p) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$checked", store.LastChecked is DateTime d ? FormatTimestamp(d) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$prime", store.IsPrime ? 1 : 0);
        command.Parameters.AddWithValue("$available", store.IsAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$promo", store.HasPromo ? 1 : 0);
        command.Parameters.AddWithValue("$preorder", store.IsPreorder ? 1 : 0);
        command.Parameters.AddWithValue("$selector", (object?)store.PriceSelector ?? DBNull.Value);
        command.ExecuteNonQuery();
        store.Id = LastInsertRowId(connection, transaction);
    }

    private static long LastInsertRowId(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT last_insert_rowid();";
        return (long)command.ExecuteScalar()!;
    }

    private static string FormatPrice(decimal price) => price.ToString(CultureInfo.InvariantCulture);

    private static decimal ParsePrice(string raw) => decimal.Parse(raw, CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTime value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    private static DateTime ParseTimestamp(string raw) => DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    #endregion
}
