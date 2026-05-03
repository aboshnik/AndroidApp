package com.example.app

import android.os.Bundle
import android.view.LayoutInflater
import android.view.View
import android.view.WindowManager
import android.view.animation.AnimationUtils
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import androidx.activity.result.contract.ActivityResultContracts
import coil.load
import com.example.app.api.ApiClient
import com.example.app.api.CoinsCartItem
import com.example.app.api.CoinsShopItem
import com.example.app.api.ShopCartAddRequest
import com.example.app.api.ShopCheckoutRequest
import com.example.app.api.ShopCartSetQuantityRequest
import com.example.app.api.ShopProductCreateRequest
import com.example.app.api.ShopOrderItem
import com.example.app.api.ShopOrderDetailItem
import com.example.app.api.ShopOrderDetailsItem
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.RequestBody.Companion.asRequestBody
import java.io.File

class ShopActivity : BaseActivity() {
    private var login: String = ""
    private var isTechAdmin: Boolean = false
    private lateinit var tvBalance: TextView
    private lateinit var tvBalanceCompact: TextView
    private lateinit var shopCategories: LinearLayout
    private lateinit var btnHeaderCart: ImageView
    private lateinit var btnHeaderOrders: ImageView
    private lateinit var btnHeaderAddProduct: ImageView
    private lateinit var cartPanel: View
    private lateinit var shopBalanceCard: View
    private lateinit var productsSection: View
    private lateinit var ordersSection: View
    private lateinit var cartItemsContainer: LinearLayout
    private lateinit var productsContainer: LinearLayout
    private lateinit var ordersContainer: LinearLayout
    private lateinit var tvCartTotal: TextView
    private lateinit var btnCheckout: TextView
    private var activeTab: ShopTab = ShopTab.BALANCE
    private var activeCategory: String = "Все"
    private var allProducts: List<CoinsShopItem> = emptyList()
    private var pendingProductImageUri: android.net.Uri? = null
    private var pendingProductImageCallback: ((android.net.Uri) -> Unit)? = null
    private var isUploadingProductImage: Boolean = false
    private val pickProductImage = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri != null) pendingProductImageCallback?.invoke(uri)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_shop)
        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        login = auth.getString("login", "")?.trim().orEmpty()
        isTechAdmin = auth.getBoolean("isTechAdmin", false)

        tvBalance = findViewById(R.id.tvShopBalance)
        tvBalanceCompact = findViewById(R.id.tvShopBalanceCompact)
        shopCategories = findViewById(R.id.shopCategories)
        btnHeaderCart = findViewById(R.id.btnHeaderCart)
        btnHeaderOrders = findViewById(R.id.btnHeaderOrders)
        btnHeaderAddProduct = findViewById(R.id.btnHeaderAddProduct)
        cartPanel = findViewById(R.id.cartPanel)
        shopBalanceCard = findViewById(R.id.shopBalanceCard)
        productsSection = findViewById(R.id.productsSection)
        ordersSection = findViewById(R.id.ordersSection)
        cartItemsContainer = findViewById(R.id.cartItemsContainer)
        productsContainer = findViewById(R.id.productsContainer)
        ordersContainer = findViewById(R.id.ordersContainer)
        tvCartTotal = findViewById(R.id.tvCartTotal)
        btnCheckout = findViewById(R.id.btnCheckout)

        findViewById<TextView>(R.id.tvShopBalanceCompact).setOnClickListener { setActiveTab(ShopTab.BALANCE) }
        btnHeaderOrders.setOnClickListener {
            setActiveTab(if (activeTab == ShopTab.ORDERS) ShopTab.BALANCE else ShopTab.ORDERS)
        }
        btnHeaderCart.setOnClickListener {
            setActiveTab(if (activeTab == ShopTab.CART) ShopTab.BALANCE else ShopTab.CART)
        }
        btnHeaderAddProduct.visibility = if (isTechAdmin) View.VISIBLE else View.GONE
        btnHeaderAddProduct.setOnClickListener { showAddProductDialog() }
        btnCheckout.setOnClickListener { checkoutCart() }
        setupCategoryChips(emptyList())
        setActiveTab(ShopTab.BALANCE)
        scope.launch { loadAll() }
    }

    private fun setActiveTab(tab: ShopTab) {
        activeTab = tab
        btnHeaderOrders.alpha = if (tab == ShopTab.ORDERS) 1f else 0.75f
        btnHeaderCart.alpha = if (tab == ShopTab.CART) 1f else 0.75f

        when (tab) {
            ShopTab.BALANCE -> {
                shopBalanceCard.visibility = View.GONE
                productsSection.visibility = View.VISIBLE
                ordersSection.visibility = View.GONE
                cartPanel.visibility = View.GONE
            }
            ShopTab.ORDERS -> {
                shopBalanceCard.visibility = View.GONE
                productsSection.visibility = View.GONE
                ordersSection.visibility = View.VISIBLE
                cartPanel.visibility = View.GONE
            }
            ShopTab.CART -> {
                shopBalanceCard.visibility = View.GONE
                productsSection.visibility = View.GONE
                ordersSection.visibility = View.GONE
                cartPanel.visibility = View.VISIBLE
            }
        }
    }

    private suspend fun loadAll() {
        if (login.isBlank()) return
        try {
            val balanceResp = withContext(Dispatchers.IO) { ApiClient.coinsApi.getBalance(login) }
            val shopResp = withContext(Dispatchers.IO) { ApiClient.coinsApi.getShop(login) }
            val cartResp = withContext(Dispatchers.IO) { ApiClient.coinsApi.getCart(login) }
            val ordersResp = withContext(Dispatchers.IO) { ApiClient.coinsApi.getOrders(login) }

            val balanceBody = balanceResp.body()
            if (balanceResp.isSuccessful && balanceBody?.success == true && balanceBody.balance != null) {
                tvBalance.text = "Баланс: ${balanceBody.balance.balance}"
                tvBalanceCompact.text = balanceBody.balance.balance.toString()
            }

            val products = if (shopResp.isSuccessful) shopResp.body()?.items.orEmpty() else emptyList()
            allProducts = products
            setupCategoryChips(products)
            renderProducts(filteredProducts())

            val cart = if (cartResp.isSuccessful) cartResp.body()?.items.orEmpty() else emptyList()
            val total = if (cartResp.isSuccessful) cartResp.body()?.totalAmount ?: 0 else 0
            renderCart(cart, total)
            val orders = if (ordersResp.isSuccessful) ordersResp.body()?.items.orEmpty() else emptyList()
            renderOrders(orders)
        } catch (e: Exception) {
            safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
        }
    }

    private fun renderOrders(items: List<ShopOrderItem>) {
        ordersContainer.removeAllViews()
        val inflater = LayoutInflater.from(this)
        if (items.isEmpty()) {
            val empty = TextView(this).apply {
                text = "Покупок пока нет"
                setTextColor(getColor(R.color.text_secondary))
                textSize = 13f
            }
            ordersContainer.addView(empty)
            return
        }
        for (item in items) {
            val row = inflater.inflate(R.layout.item_shop_order, ordersContainer, false)
            row.findViewById<TextView>(R.id.tvOrderNumber).text = item.orderNumber
            row.findViewById<TextView>(R.id.tvOrderMeta).text = "${item.createdAt.replace('T', ' ').take(16)} · ${item.itemsCount} шт."
            row.findViewById<TextView>(R.id.tvOrderTotal).text = item.totalAmount.toString()
            row.setOnClickListener { showOrderDetails(item.orderNumber) }
            ordersContainer.addView(row)
        }
    }

    private fun showOrderDetails(orderNumber: String) {
        if (login.isBlank() || orderNumber.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.coinsApi.getOrderDetails(login = login, orderNumber = orderNumber)
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success || body.order == null) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                showOrderDetailsDialog(body.order)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun showOrderDetailsDialog(order: ShopOrderDetailsItem) {
        val view = layoutInflater.inflate(R.layout.dialog_shop_order_details, null, false)
        val tvNumber = view.findViewById<TextView>(R.id.tvDialogOrderNumber)
        val tvMeta = view.findViewById<TextView>(R.id.tvDialogOrderMeta)
        val tvTotal = view.findViewById<TextView>(R.id.tvDialogOrderTotal)
        val btnClose = view.findViewById<TextView>(R.id.btnDialogOrderClose)
        val itemsContainer = view.findViewById<LinearLayout>(R.id.orderDetailsItemsContainer)

        tvNumber.text = order.orderNumber
        tvMeta.text = "${order.createdAt.replace('T', ' ').take(16)} · ${order.itemsCount} шт."
        tvTotal.text = "Итого: ${order.totalAmount}"
        renderOrderDetailsItems(itemsContainer, order.items)

        val dialog = AlertDialog.Builder(this)
            .setView(view)
            .create()
        btnClose.setOnClickListener { dialog.dismiss() }
        safeShowCreatedDialog(dialog)
    }

    private fun renderOrderDetailsItems(container: LinearLayout, items: List<ShopOrderDetailItem>) {
        container.removeAllViews()
        val inflater = LayoutInflater.from(this)
        if (items.isEmpty()) {
            val empty = TextView(this).apply {
                text = "Состав заказа недоступен"
                setTextColor(getColor(R.color.text_secondary))
                textSize = 13f
            }
            container.addView(empty)
            return
        }

        items.forEach { item ->
            val row = inflater.inflate(R.layout.item_shop_order_detail, container, false)
            val iv = row.findViewById<ImageView>(R.id.ivOrderDetailPhoto)
            row.findViewById<TextView>(R.id.tvOrderDetailTitle).text = item.title
            row.findViewById<TextView>(R.id.tvOrderDetailQtyPrice).text = "${item.quantity} × ${item.price}"
            row.findViewById<TextView>(R.id.tvOrderDetailLineTotal).text = item.lineTotal.toString()
            if (!item.imageUrl.isNullOrBlank()) {
                iv.load(item.imageUrl) {
                    crossfade(true)
                    error(R.drawable.coin)
                }
            } else {
                iv.setImageResource(R.drawable.coin)
            }
            row.setOnClickListener { showOrderItemPreviewDialog(item) }
            container.addView(row)
        }
    }

    private fun showOrderItemPreviewDialog(item: ShopOrderDetailItem) {
        val view = layoutInflater.inflate(R.layout.dialog_shop_order_item_preview, null, false)
        val ivPhoto = view.findViewById<ImageView>(R.id.ivPreviewItemPhoto)
        val tvTitle = view.findViewById<TextView>(R.id.tvPreviewItemTitle)
        val tvMeta = view.findViewById<TextView>(R.id.tvPreviewItemMeta)
        val tvTotal = view.findViewById<TextView>(R.id.tvPreviewItemLineTotal)
        val btnClose = view.findViewById<TextView>(R.id.btnPreviewItemClose)

        tvTitle.text = item.title
        tvMeta.text = "Количество: ${item.quantity} × ${item.price}"
        tvTotal.text = "Сумма: ${item.lineTotal}"
        if (!item.imageUrl.isNullOrBlank()) {
            ivPhoto.load(item.imageUrl) {
                crossfade(true)
                error(R.drawable.coin)
            }
        } else {
            ivPhoto.setImageResource(R.drawable.coin)
        }

        val dialog = AlertDialog.Builder(this)
            .setView(view)
            .create()
        btnClose.setOnClickListener { dialog.dismiss() }
        safeShowCreatedDialog(dialog)
    }

    private fun renderProducts(items: List<CoinsShopItem>) {
        productsContainer.removeAllViews()
        val inflater = LayoutInflater.from(this)
        if (items.isEmpty()) {
            val empty = TextView(this).apply {
                text = "Товаров пока нет"
                setTextColor(getColor(R.color.text_secondary))
                textSize = 13f
            }
            productsContainer.addView(empty)
            return
        }
        var index = 0
        while (index < items.size) {
            val rowWrap = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MATCH_PARENT,
                    LinearLayout.LayoutParams.WRAP_CONTENT
                )
            }
            val left = buildProductCard(inflater, items[index], rowWrap)
            rowWrap.addView(left)
            if (index + 1 < items.size) {
                val right = buildProductCard(inflater, items[index + 1], rowWrap)
                val lp = right.layoutParams as LinearLayout.LayoutParams
                lp.marginStart = 8
                right.layoutParams = lp
                rowWrap.addView(right)
            } else {
                rowWrap.addView(View(this).apply {
                    layoutParams = LinearLayout.LayoutParams(0, 1, 1f)
                })
            }
            productsContainer.addView(rowWrap)
            index += 2
        }
    }

    private fun buildProductCard(inflater: LayoutInflater, item: CoinsShopItem, parent: LinearLayout): View {
        val card = inflater.inflate(R.layout.item_shop_product, parent, false)
        card.layoutParams = LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f)
        val iv = card.findViewById<ImageView>(R.id.ivProductPhoto)
        val tvTitle = card.findViewById<TextView>(R.id.tvProductTitle)
        val tvDesc = card.findViewById<TextView>(R.id.tvProductDesc)
        val tvStock = card.findViewById<TextView>(R.id.tvProductStock)
        val priceRow = card.findViewById<View>(R.id.productPriceRow)
        val tvPrice = card.findViewById<TextView>(R.id.tvProductPrice)
        val btnAdd = card.findViewById<TextView>(R.id.btnAddToCart)

        tvTitle.text = item.title
        tvDesc.text = item.description?.trim().orEmpty()
            .ifBlank { item.category?.trim().orEmpty() }
            .ifBlank { "Товары" }
        tvStock.text = if (item.stock > 0) "В наличии" else "Нет в наличии"
        tvPrice.text = item.price.toString()
        if (!item.imageUrl.isNullOrBlank()) {
            iv.load(item.imageUrl) {
                crossfade(true)
                error(R.drawable.coin)
            }
        } else {
            iv.setImageResource(R.drawable.coin)
        }
        btnAdd.isEnabled = item.stock > item.inCartQty
        btnAdd.text = if (btnAdd.isEnabled) "Купить" else "Нет в наличии"
        btnAdd.setBackgroundResource(
            if (btnAdd.isEnabled) R.drawable.bg_shop_buy_btn else R.drawable.bg_shop_out_of_stock
        )
        btnAdd.setTextColor(
            getColor(if (btnAdd.isEnabled) android.R.color.white else R.color.text_secondary)
        )
        tvPrice.alpha = if (btnAdd.isEnabled) 1f else 0.55f
        priceRow.visibility = View.VISIBLE
        btnAdd.setOnClickListener {
            if (!btnAdd.isEnabled) return@setOnClickListener
            val productId = item.id.toIntOrNull() ?: return@setOnClickListener
            addToCart(productId)
        }
        return card
    }

    private fun setupCategoryChips(items: List<CoinsShopItem>) {
        val categories = linkedSetOf("Все")
        items.mapNotNull { it.category?.trim() }
            .filter { it.isNotBlank() }
            .forEach { categories.add(it) }

        if (!categories.contains(activeCategory)) activeCategory = "Все"
        shopCategories.removeAllViews()
        categories.forEachIndexed { index, category ->
            val chip = TextView(this).apply {
                layoutParams = LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.WRAP_CONTENT,
                    dpToPx(32)
                ).also { if (index > 0) it.marginStart = dpToPx(8) }
                minWidth = dpToPx(70)
                gravity = android.view.Gravity.CENTER
                setPadding(dpToPx(14), 0, dpToPx(14), 0)
                text = category
                setBackgroundResource(R.drawable.bg_shop_category_chip)
                setTextSize(android.util.TypedValue.COMPLEX_UNIT_SP, 12f)
                isSelected = category == activeCategory
                setTypeface(null, if (isSelected) android.graphics.Typeface.BOLD else android.graphics.Typeface.NORMAL)
                setTextColor(getColor(if (isSelected) R.color.nav_active else android.R.color.white))
                setOnClickListener {
                    activeCategory = category
                    renderProducts(filteredProducts())
                    setupCategoryChips(allProducts)
                }
            }
            shopCategories.addView(chip)
        }
    }

    private fun filteredProducts(): List<CoinsShopItem> {
        if (activeCategory == "Все") return allProducts
        return allProducts.filter { it.category?.trim().orEmpty() == activeCategory }
    }

    private fun dpToPx(dp: Int): Int = (dp * resources.displayMetrics.density).toInt()

    private fun renderCart(items: List<CoinsCartItem>, total: Int) {
        cartItemsContainer.removeAllViews()
        val inflater = LayoutInflater.from(this)
        if (items.isEmpty()) {
            val empty = TextView(this).apply {
                text = "Корзина пуста"
                setTextColor(getColor(R.color.text_secondary))
                textSize = 13f
            }
            cartItemsContainer.addView(empty)
        } else {
            for (item in items) {
                val row = inflater.inflate(R.layout.item_shop_cart, cartItemsContainer, false)
                val iv = row.findViewById<ImageView>(R.id.ivCartPhoto)
                row.findViewById<TextView>(R.id.tvCartTitle).text = item.title
                row.findViewById<TextView>(R.id.tvCartQty).text = "Количество: ${item.quantity} × ${item.price}"
                row.findViewById<TextView>(R.id.tvCartQtyInline).text = item.quantity.toString()
                row.findViewById<TextView>(R.id.tvCartLineTotal).text = item.lineTotal.toString()
                val btnMinus = row.findViewById<TextView>(R.id.btnCartMinus)
                val btnPlus = row.findViewById<TextView>(R.id.btnCartPlus)
                if (!item.imageUrl.isNullOrBlank()) {
                    iv.load(item.imageUrl) { crossfade(true); error(R.drawable.coin) }
                } else {
                    iv.setImageResource(R.drawable.coin)
                }
                btnMinus.setOnClickListener {
                    val next = (item.quantity - 1).coerceAtLeast(0)
                    updateCartQty(item.productId, next)
                }
                btnPlus.setOnClickListener {
                    val next = item.quantity + 1
                    updateCartQty(item.productId, next)
                }
                cartItemsContainer.addView(row)
            }
        }
        tvCartTotal.text = "Итого: $total"
        btnCheckout.isEnabled = items.isNotEmpty()
        btnCheckout.alpha = if (items.isNotEmpty()) 1f else 0.6f
    }

    private fun addToCart(productId: Int) {
        if (login.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.coinsApi.addToCart(ShopCartAddRequest(login = login, productId = productId, quantity = 1))
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast("Добавлено в корзину")
                loadAll()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun showAddProductDialog() {
        if (!isTechAdmin) return
        val view = layoutInflater.inflate(R.layout.dialog_add_shop_product, null, false)
        val ivPreview = view.findViewById<ImageView>(R.id.ivDialogProductPreview)
        val btnPick = view.findViewById<TextView>(R.id.btnPickProductImage)
        val etTitle = view.findViewById<android.widget.EditText>(R.id.etDialogProductTitle)
        val etDesc = view.findViewById<android.widget.EditText>(R.id.etDialogProductDesc)
        val etPrice = view.findViewById<android.widget.EditText>(R.id.etDialogProductPrice)
        val etStock = view.findViewById<android.widget.EditText>(R.id.etDialogProductStock)
        val tvUploadState = view.findViewById<TextView>(R.id.tvUploadState)
        val btnCancel = view.findViewById<TextView>(R.id.btnDialogCancel)
        val btnSave = view.findViewById<TextView>(R.id.btnDialogSave)
        pendingProductImageUri = null
        ivPreview.setImageResource(R.drawable.coin)

        val dialog = AlertDialog.Builder(this)
            .setView(view)
            .create()
        dialog.window?.setBackgroundDrawableResource(android.R.color.transparent)
        dialog.window?.setLayout(
            WindowManager.LayoutParams.MATCH_PARENT,
            WindowManager.LayoutParams.WRAP_CONTENT
        )
        dialog.window?.setGravity(android.view.Gravity.BOTTOM)

        val dismissWithAnim = {
            if (dialog.isShowing) {
                val out = AnimationUtils.loadAnimation(this, R.anim.sheet_slide_out_down)
                out.setAnimationListener(object : android.view.animation.Animation.AnimationListener {
                    override fun onAnimationStart(animation: android.view.animation.Animation?) {}
                    override fun onAnimationEnd(animation: android.view.animation.Animation?) { dialog.dismiss() }
                    override fun onAnimationRepeat(animation: android.view.animation.Animation?) {}
                })
                view.startAnimation(out)
            }
        }

        btnPick.setOnClickListener {
            pendingProductImageCallback = { uri ->
                pendingProductImageUri = uri
                ivPreview.load(uri) {
                    crossfade(true)
                    error(R.drawable.coin)
                }
            }
            pickProductImage.launch(arrayOf("image/*"))
        }
        btnCancel.setOnClickListener { dismissWithAnim.invoke() }
        btnSave.setOnClickListener {
            if (isUploadingProductImage) return@setOnClickListener
            val title = etTitle.text?.toString()?.trim().orEmpty()
            val desc = etDesc.text?.toString()?.trim()
            val price = etPrice.text?.toString()?.trim()?.toIntOrNull() ?: 0
            val stock = etStock.text?.toString()?.trim()?.toIntOrNull() ?: -1
            if (title.isBlank() || price <= 0 || stock < 0) {
                safeToast("Заполните название, цену и остаток", long = true)
                return@setOnClickListener
            }
            createProduct(
                title = title,
                description = desc,
                price = price,
                stock = stock,
                imageUri = pendingProductImageUri,
                onSuccess = { dismissWithAnim.invoke() },
                onUploadingChanged = { uploading ->
                    tvUploadState.visibility = if (uploading) View.VISIBLE else View.GONE
                    btnSave.isEnabled = !uploading
                    btnPick.isEnabled = !uploading
                    btnCancel.isEnabled = !uploading
                }
            )
        }
        safeShowCreatedDialog(dialog)
        view.startAnimation(AnimationUtils.loadAnimation(this, R.anim.sheet_slide_in_up))
    }

    private fun createProduct(
        title: String,
        description: String?,
        price: Int,
        stock: Int,
        imageUri: android.net.Uri?,
        onSuccess: (() -> Unit)? = null,
        onUploadingChanged: ((Boolean) -> Unit)? = null
    ) {
        scope.launch {
            try {
                isUploadingProductImage = imageUri != null
                onUploadingChanged?.invoke(isUploadingProductImage)
                val uploadedImageUrl = if (imageUri != null) uploadProductImage(imageUri) else null
                isUploadingProductImage = false
                onUploadingChanged?.invoke(false)
                if (imageUri != null && uploadedImageUrl.isNullOrBlank()) return@launch
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.coinsApi.createProduct(
                        ShopProductCreateRequest(
                            login = login,
                            title = title,
                            description = description,
                            price = price,
                            stock = stock,
                            imageUrl = uploadedImageUrl
                        )
                    )
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast("Товар добавлен")
                loadAll()
                onSuccess?.invoke()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            } finally {
                isUploadingProductImage = false
                onUploadingChanged?.invoke(false)
            }
        }
    }

    private fun updateCartQty(productId: Int, quantity: Int) {
        if (login.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.coinsApi.setCartQuantity(
                        ShopCartSetQuantityRequest(login = login, productId = productId, quantity = quantity)
                    )
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                // instant refresh of totals/rows
                renderCart(body.items.orEmpty(), body.totalAmount)
                loadAll()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun checkoutCart() {
        if (login.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.coinsApi.checkout(ShopCheckoutRequest(login))
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast("Заказ оформлен: ${body.itemsCount} шт., списано ${body.totalSpent}")
                loadAll()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private suspend fun uploadProductImage(uri: android.net.Uri): String? {
        return try {
            val mime = contentResolver.getType(uri) ?: "image/jpeg"
            val ext = when {
                mime.contains("png", true) -> ".png"
                mime.contains("webp", true) -> ".webp"
                mime.contains("gif", true) -> ".gif"
                else -> ".jpg"
            }
            val temp = File(cacheDir, "shop_product_upload$ext")
            contentResolver.openInputStream(uri)?.use { input ->
                temp.outputStream().use { output -> input.copyTo(output) }
            } ?: run {
                safeToast("Не удалось прочитать изображение", long = true)
                return null
            }
            val part = MultipartBody.Part.createFormData(
                "file",
                temp.name,
                temp.asRequestBody(mime.toMediaTypeOrNull())
            )
            val resp = withContext(Dispatchers.IO) {
                ApiClient.coinsApi.uploadProductImage(login = login, file = part)
            }
            runCatching { temp.delete() }
            val body = resp.body()
            if (!resp.isSuccessful || body == null || !body.success || body.imageUrl.isNullOrBlank()) {
                safeToast(body?.message ?: getString(R.string.error_network), long = true)
                null
            } else {
                body.imageUrl
            }
        } catch (e: Exception) {
            safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            null
        }
    }

    private fun safeShowCreatedDialog(dialog: AlertDialog) {
        if (!canShowUi()) return
        try {
            dialog.show()
        } catch (_: android.view.WindowManager.BadTokenException) {
            // ignore: activity is not in valid window state
        }
    }

    private enum class ShopTab {
        BALANCE,
        ORDERS,
        CART
    }
}

