package com.example.app.chats

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.Editable
import android.text.TextWatcher
import android.view.Menu
import android.view.MenuItem
import android.view.View
import android.widget.EditText
import android.widget.ImageView
import android.widget.ProgressBar
import android.widget.TextView
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.view.ActionMode
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.app.BaseActivity
import com.example.app.ChatEvents
import com.example.app.HomeActivity
import com.example.app.ProfileActivity
import com.example.app.R
import com.example.app.SettingsActivity
import com.example.app.SessionManager
import com.example.app.api.ApiClient
import com.example.app.api.ColleagueItem
import com.example.app.api.EmployeeProfile
import com.example.app.api.OpenDirectThreadRequest
import com.example.app.api.ThreadItem
import coil.load
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

class ChatsActivity : BaseActivity() {
    override fun swipeTabIndex(): Int = 1
    private lateinit var recycler: RecyclerView
    private lateinit var progress: ProgressBar
    private lateinit var empty: TextView
    private lateinit var adapter: ThreadsAdapter
    private var searchJob: Job? = null
    private var threadsActionMode: ActionMode? = null

    private var employeeId: String = ""
    private var allThreads: List<ThreadItem> = emptyList()
    private var activeFilter: ThreadsFilter = ThreadsFilter.ALL

    private enum class ThreadsFilter { ALL, DISCUSSIONS, CATALOG }

    private val mainHandler = Handler(Looper.getMainLooper())
    private val threadsRefreshIntervalMs = 2500L
    private val threadsRefreshRunnable = object : Runnable {
        override fun run() {
            if (canShowUi()) loadThreads(silent = true)
            mainHandler.postDelayed(this, threadsRefreshIntervalMs)
        }
    }
    private val refreshAfterPushRunnable = Runnable {
        if (!canShowUi()) return@Runnable
        loadThreads(silent = true)
    }
    private val threadsPushReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            if (intent?.action != ChatEvents.ACTION_REFRESH_THREAD_LIST) return
            mainHandler.removeCallbacks(refreshAfterPushRunnable)
            mainHandler.postDelayed(refreshAfterPushRunnable, 400L)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_chats)

        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        employeeId = auth.getString("employeeId", "")?.trim().orEmpty()

        recycler = findViewById(R.id.recyclerThreads)
        progress = findViewById(R.id.threadsProgress)
        empty = findViewById(R.id.threadsEmpty)

        val selectionCallback = object : ActionMode.Callback {
            override fun onCreateActionMode(mode: ActionMode?, menu: Menu?): Boolean {
                menuInflater.inflate(R.menu.menu_chats_selection, menu)
                return true
            }

            override fun onPrepareActionMode(mode: ActionMode?, menu: Menu?) = false

            override fun onActionItemClicked(mode: ActionMode?, item: MenuItem?): Boolean {
                return when (item?.itemId) {
                    R.id.action_clear_thread_history -> {
                        confirmAndClearSelectedThreads { mode?.finish() }
                        true
                    }
                    else -> false
                }
            }

            override fun onDestroyActionMode(mode: ActionMode?) {
                threadsActionMode = null
                adapter.exitSelectionMode()
            }
        }

        adapter = ThreadsAdapter(
            onOpen = { thread -> openThread(thread) },
            onSelectionStarted = {
                if (threadsActionMode == null) {
                    threadsActionMode = startSupportActionMode(selectionCallback)
                }
            },
            onSelectionCount = { n -> threadsActionMode?.title = "$n выбрано" },
            onSelectionEmpty = { threadsActionMode?.finish() }
        )
        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = adapter

        // Tabs: ALL / DISCUSSIONS / CATALOG
        val tabAll = findViewById<TextView>(R.id.tabAll)
        val tabDiscussions = findViewById<TextView>(R.id.tabDiscussions)
        val tabCatalog = findViewById<TextView>(R.id.tabCatalog)
        val indicator = findViewById<View>(R.id.tabIndicator)
        fun select(filter: ThreadsFilter) {
            activeFilter = filter
            val active = getColor(R.color.nav_active)
            val inactive = getColor(R.color.nav_inactive)
            tabAll.setTextColor(if (filter == ThreadsFilter.ALL) active else inactive)
            tabDiscussions.setTextColor(if (filter == ThreadsFilter.DISCUSSIONS) active else inactive)
            tabCatalog.setTextColor(if (filter == ThreadsFilter.CATALOG) active else inactive)

            val target = when (filter) {
                ThreadsFilter.ALL -> tabAll
                ThreadsFilter.DISCUSSIONS -> tabDiscussions
                ThreadsFilter.CATALOG -> tabCatalog
            }
            target.post {
                val lp = indicator.layoutParams as androidx.constraintlayout.widget.ConstraintLayout.LayoutParams
                lp.startToStart = target.id
                lp.endToEnd = target.id
                indicator.layoutParams = lp
                applyFilter()
            }
        }
        tabAll.setOnClickListener { select(ThreadsFilter.ALL) }
        tabDiscussions.setOnClickListener { select(ThreadsFilter.DISCUSSIONS) }
        tabCatalog.setOnClickListener { select(ThreadsFilter.CATALOG) }
        select(ThreadsFilter.ALL)

        findViewById<View>(R.id.fabNewChat).setOnClickListener { openColleagueSearchDialog() }
        findViewById<View>(R.id.btnSearch).setOnClickListener { openColleagueSearchDialog() }
        findViewById<View>(R.id.navHome).setOnClickListener {
            setBottomTab("home")
            startActivity(Intent(this, HomeActivity::class.java))
        }
        findViewById<View>(R.id.navChats).setOnClickListener {
            setBottomTab("chats")
        }
        findViewById<View>(R.id.navSettings).setOnClickListener {
            setBottomTab("settings")
            startActivity(Intent(this, SettingsActivity::class.java))
        }
        findViewById<View>(R.id.navProfile).setOnClickListener {
            setBottomTab("profile")
            startActivity(Intent(this, ProfileActivity::class.java))
        }
        findViewById<View>(R.id.navContacts).setOnClickListener {
            setBottomTab("store")
            startActivity(Intent(this, com.example.app.ShopActivity::class.java))
        }

        setBottomTab("chats")

        loadThreads()
    }

    override fun onStart() {
        super.onStart()
        ContextCompat.registerReceiver(
            this,
            threadsPushReceiver,
            IntentFilter(ChatEvents.ACTION_REFRESH_THREAD_LIST),
            ContextCompat.RECEIVER_NOT_EXPORTED
        )
    }

    override fun onStop() {
        mainHandler.removeCallbacks(threadsRefreshRunnable)
        mainHandler.removeCallbacks(refreshAfterPushRunnable)
        try {
            unregisterReceiver(threadsPushReceiver)
        } catch (_: IllegalArgumentException) {
        }
        super.onStop()
    }

    override fun onResume() {
        super.onResume()
        // When coming back from other screens, keep Chats active
        setBottomTab("chats")
        loadThreads()
        mainHandler.removeCallbacks(threadsRefreshRunnable)
        mainHandler.postDelayed(threadsRefreshRunnable, threadsRefreshIntervalMs)
    }

    override fun onPause() {
        mainHandler.removeCallbacks(threadsRefreshRunnable)
        super.onPause()
    }

    private fun setBottomTab(tab: String) {
        val active = getColor(R.color.nav_active)
        val inactive = getColor(R.color.nav_inactive)

        fun setItem(containerId: Int, iconId: Int, textId: Int, isActive: Boolean, tintProfile: Boolean = true) {
            val container = findViewById<View>(containerId)
            val icon = findViewById<ImageView>(iconId)
            val text = findViewById<TextView>(textId)
            val c = if (isActive) active else inactive
            if (tintProfile || iconId != R.id.navProfileIcon) {
                icon.setColorFilter(c)
            } else {
                icon.setColorFilter(c)
            }
            text.setTextColor(c)
            text.setTypeface(null, if (isActive) android.graphics.Typeface.BOLD else android.graphics.Typeface.NORMAL)
            container.setBackgroundResource(
                if (isActive) R.drawable.bg_bottom_nav_item_active
                else android.R.color.transparent
            )
        }

        setItem(R.id.navChats, R.id.navChatsIcon, R.id.navChatsText, tab == "chats")
        setItem(R.id.navHome, R.id.navHomeIcon, R.id.navHomeText, tab == "home")
        setItem(R.id.navSettings, R.id.navSettingsIcon, R.id.navSettingsText, tab == "settings")
        setItem(R.id.navProfile, R.id.navProfileIcon, R.id.navProfileText, tab == "profile")
        setItem(R.id.navContacts, R.id.navContactsIcon, R.id.navContactsText, tab == "store")
    }

    private fun applyFilter() {
        val filtered = when (activeFilter) {
            ThreadsFilter.ALL -> allThreads
            ThreadsFilter.DISCUSSIONS -> allThreads.filter { t ->
                val type = t.type?.lowercase() ?: ""
                type != "bot" && type != "channel"
            }
            ThreadsFilter.CATALOG -> allThreads.filter { t ->
                val type = t.type?.lowercase() ?: ""
                type == "bot" || type == "channel"
            }
        }
        adapter.submit(filtered)
    }

    private fun showContactsDialog() {
        val view = layoutInflater.inflate(R.layout.dialog_contacts, null, false)
        safeShowDialog(
            AlertDialog.Builder(this)
                .setView(view)
                .setPositiveButton("Закрыть", null)
        )
    }

    private fun openThread(thread: ThreadItem) {
        val i = Intent(this, ChatActivity::class.java)
        i.putExtra(ChatActivity.EXTRA_THREAD_ID, thread.id)
        i.putExtra(ChatActivity.EXTRA_THREAD_TITLE, thread.title)
        i.putExtra(ChatActivity.EXTRA_THREAD_TYPE, thread.type)
        i.putExtra(ChatActivity.EXTRA_THREAD_IS_TECH_ADMIN, thread.isTechAdmin)
        i.putExtra(ChatActivity.EXTRA_THREAD_BOT_ID, thread.botId)
        i.putExtra(ChatActivity.EXTRA_THREAD_IS_OFFICIAL_BOT, thread.isOfficialBot)
        i.putExtra(ChatActivity.EXTRA_THREAD_IS_ONLINE, thread.isOnline)
        i.putExtra(ChatActivity.EXTRA_THREAD_AVATAR_URL, thread.avatarUrl)
        startActivity(i)
    }

    private fun loadThreads(silent: Boolean = false) {
        if (employeeId.isBlank()) {
            empty.text = getString(R.string.error_network)
            empty.visibility = View.VISIBLE
            return
        }

        if (!silent) {
            progress.visibility = View.VISIBLE
            empty.visibility = View.GONE
            findViewById<TextView>(R.id.tvChatsSubtitle).text = "Обновление..."
        }

        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.getThreads(login = employeeId)
                }
                if (!silent) progress.visibility = View.GONE
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    empty.text = body?.message ?: getString(R.string.error_network)
                    empty.visibility = View.VISIBLE
                    if (!silent) adapter.submit(emptyList())
                    if (!silent) findViewById<TextView>(R.id.tvChatsSubtitle).text = "Групповые и персональные"
                    return@launch
                }
                val list = body.threads.orEmpty().sortedByDescending { threadActivityMillis(it) }
                allThreads = list
                applyFilter()
                updateChatsBottomBadge(list)
                empty.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
                if (list.isEmpty()) empty.text = "Нет диалогов"
                if (!silent) findViewById<TextView>(R.id.tvChatsSubtitle).text = "Групповые и персональные"
            } catch (e: Exception) {
                if (!silent) {
                    progress.visibility = View.GONE
                    empty.text = "${getString(R.string.error_network)} ${e.message}"
                    empty.visibility = View.VISIBLE
                    adapter.submit(emptyList())
                    updateChatsBottomBadge(emptyList())
                    findViewById<TextView>(R.id.tvChatsSubtitle).text = "Проблемы с сетью"
                }
            }
        }
    }

    /** Для стабильного порядка «самый свежий чат сверху» даже при расхождении формата дат. */
    private fun threadActivityMillis(t: ThreadItem): Long {
        val raw = t.lastMessageAtUtc?.trim().orEmpty().ifBlank { t.createdAtUtc.trim() }
        if (raw.isEmpty()) return 0L
        runCatching { return OffsetDateTime.parse(raw).toInstant().toEpochMilli() }
        runCatching {
            val n = raw.replace(' ', 'T')
            return LocalDateTime.parse(n, DateTimeFormatter.ISO_LOCAL_DATE_TIME)
                .atZone(ZoneOffset.UTC).toInstant().toEpochMilli()
        }
        if (raw.length >= 10) {
            runCatching {
                return LocalDate.parse(raw.substring(0, 10))
                    .atStartOfDay(ZoneOffset.UTC).toInstant().toEpochMilli()
            }
        }
        return 0L
    }

    private fun updateChatsBottomBadge(threads: List<ThreadItem>) {
        val badge = findViewById<TextView>(R.id.navChatsBadge)
        val unread = threads.sumOf { it.unreadCount.coerceAtLeast(0) }
        if (unread <= 0) {
            badge.visibility = View.GONE
            return
        }
        badge.visibility = View.VISIBLE
        badge.text = if (unread > 99) "99+" else unread.toString()
    }

    private fun confirmAndClearSelectedThreads(onDone: () -> Unit) {
        val ids = adapter.getSelectedThreadIds()
        if (ids.isEmpty()) {
            onDone()
            return
        }
        val msg = if (ids.size == 1) {
            "Удалить все сообщения в этом чате на вашей стороне? У собеседника история не изменится."
        } else {
            "Удалить все сообщения в ${ids.size} выбранных чатах на вашей стороне? У собеседников история не изменится."
        }
        safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle("Очистить историю")
                .setMessage(msg)
                .setPositiveButton("Очистить") { _, _ ->
                    scope.launch {
                        for (id in ids) {
                            try {
                                val resp = withContext(Dispatchers.IO) {
                                    ApiClient.chatApi.clearThreadHistory(threadId = id, login = employeeId)
                                }
                                val body = resp.body()
                                if (!resp.isSuccessful || body == null || !body.success) {
                                    val hint = body?.message
                                        ?: resp.errorBody()?.use { it.string().trim() }?.take(280)
                                    safeToast(hint.takeUnless { it.isNullOrBlank() } ?: getString(R.string.error_network))
                                    break
                                }
                            } catch (e: Exception) {
                                safeToast("${getString(R.string.error_network)} ${e.message}")
                                break
                            }
                        }
                        loadThreads()
                        onDone()
                    }
                }
                .setNegativeButton("Отмена", null)
        )
    }

    private fun openColleagueSearchDialog() {
        val dialogView = layoutInflater.inflate(R.layout.dialog_colleague_search, null, false)
        val etQuery = dialogView.findViewById<EditText>(R.id.etColleagueQuery)
        val recycler = dialogView.findViewById<RecyclerView>(R.id.recyclerColleagues)
        val progress = dialogView.findViewById<ProgressBar>(R.id.colleaguesProgress)
        val empty = dialogView.findViewById<TextView>(R.id.colleaguesEmpty)

        val colleaguesAdapter = ColleaguesAdapter { colleague, dialog ->
            openColleagueProfile(colleague, dialog)
        }
        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = colleaguesAdapter

        val dialog = safeShowDialog(
            AlertDialog.Builder(this)
                .setTitle("Найти коллегу")
                .setView(dialogView)
                .setNegativeButton("Закрыть", null)
        ) ?: return
        colleaguesAdapter.dialogProvider = { dialog }

        fun performSearch(query: String) {
            searchJob?.cancel()
            searchJob = scope.launch {
                progress.visibility = View.VISIBLE
                empty.visibility = View.GONE
                try {
                    val resp = withContext(Dispatchers.IO) {
                        ApiClient.chatApi.searchColleagues(login = employeeId, query = query)
                    }
                    progress.visibility = View.GONE
                    val body = resp.body()
                    if (!resp.isSuccessful || body == null || !body.success) {
                        colleaguesAdapter.submit(emptyList())
                        empty.text = body?.message ?: getString(R.string.error_network)
                        empty.visibility = View.VISIBLE
                        return@launch
                    }
                    val list = body.colleagues.orEmpty()
                    colleaguesAdapter.submit(list)
                    empty.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
                    if (list.isEmpty()) empty.text = "Никого не найдено"
                } catch (e: Exception) {
                    progress.visibility = View.GONE
                    colleaguesAdapter.submit(emptyList())
                    empty.text = "${getString(R.string.error_network)} ${e.message}"
                    empty.visibility = View.VISIBLE
                }
            }
        }

        etQuery.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                performSearch(s?.toString()?.trim().orEmpty())
            }
            override fun afterTextChanged(s: Editable?) {}
        })
        performSearch("")
    }

    private fun openColleagueProfile(colleague: ColleagueItem, searchDialog: AlertDialog) {
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.employeeApi.getProfile(login = colleague.login)
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success || body.profile == null) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                showColleagueProfileDialog(colleague, body.profile, searchDialog)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun showColleagueProfileDialog(colleague: ColleagueItem, profile: EmployeeProfile, searchDialog: AlertDialog) {
        val view = layoutInflater.inflate(R.layout.dialog_colleague_profile, null, false)
        val btnClose = view.findViewById<View>(R.id.btnCloseColleagueProfile)
        val ivAvatar = view.findViewById<ImageView>(R.id.ivColleagueProfileAvatar)
        val vPresence = view.findViewById<View>(R.id.vColleagueProfilePresenceDot)
        val tvName = view.findViewById<TextView>(R.id.tvColleagueProfileName)
        val tvStatus = view.findViewById<TextView>(R.id.tvColleagueProfileStatus)
        val tvSubtitle = view.findViewById<TextView>(R.id.tvColleagueProfileSubtitle)
        val tvPhone = view.findViewById<TextView>(R.id.tvColleagueProfilePhone)
        val tvPosition = view.findViewById<TextView>(R.id.tvColleagueProfilePosition)
        val btnWrite = view.findViewById<View>(R.id.btnColleagueWrite)
        val btnCall = view.findViewById<View>(R.id.btnColleagueCall)

        tvName.text = profile.lastName + " " + profile.firstName
        tvStatus.text = if (colleague.isOnline) "в сети" else "не в сети"
        vPresence.setBackgroundResource(
            if (colleague.isOnline) R.drawable.bg_presence_online else R.drawable.bg_presence_offline
        )
        tvSubtitle.text = buildString {
            append("@${colleague.login}")
            if (profile.employeeId.isNotBlank()) append(" • ${profile.employeeId}")
            if (colleague.isTechAdmin) append(" • техадмин")
        }
        tvPhone.text = formatPhoneForProfile(profile.phone).ifBlank { "Телефон не указан" }
        tvPosition.text = profile.position.ifBlank { "Не указана" }

        if (!profile.avatarUrl.isNullOrBlank()) {
            ivAvatar.load(profile.avatarUrl) {
                crossfade(true)
                error(R.drawable.ic_launcher_simple)
            }
        } else {
            ivAvatar.setImageResource(R.drawable.ic_launcher_simple)
        }

        val profileDialog = safeShowDialog(
            AlertDialog.Builder(this)
                .setView(view)
        ) ?: return

        btnClose.setOnClickListener { profileDialog.dismiss() }
        btnWrite.setOnClickListener {
            profileDialog.dismiss()
            searchDialog.dismiss()
            openDirectThread(colleague)
        }
        btnCall.setOnClickListener {
            val phone = profile.phone.trim()
            if (phone.isBlank()) {
                safeToast("У коллеги не указан телефон", long = true)
                return@setOnClickListener
            }
            openDialer(phone)
        }
    }

    private fun openDialer(phoneRaw: String) {
        val phone = formatPhoneForProfile(phoneRaw)
        if (phone.isBlank()) {
            safeToast("У коллеги не указан телефон", long = true)
            return
        }
        val intent = Intent(Intent.ACTION_DIAL, Uri.parse("tel:${Uri.encode(phone)}"))
        try {
            startActivity(intent)
        } catch (_: Exception) {
            safeToast("Не удалось открыть набор номера", long = true)
        }
    }

    private fun formatPhoneForProfile(phoneRaw: String?): String {
        val digits = phoneRaw.orEmpty().filter { it.isDigit() }
        if (digits.isBlank()) return ""
        val normalized = when {
            digits.length == 11 && digits.startsWith("8") -> "7" + digits.substring(1)
            digits.length == 10 -> "7$digits"
            digits.length == 11 && digits.startsWith("7") -> digits
            else -> digits
        }
        return if (normalized.startsWith("7")) "+$normalized" else "+7$normalized"
    }

    private fun openDirectThread(colleague: ColleagueItem) {
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.openDirectThread(
                        OpenDirectThreadRequest(
                            login = employeeId,
                            colleagueLogin = colleague.login
                        )
                    )
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success || body.thread == null) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                openThread(body.thread)
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private class ColleaguesAdapter(
        private val onClick: (ColleagueItem, AlertDialog) -> Unit
    ) : RecyclerView.Adapter<ColleaguesAdapter.VH>() {
        private val items = mutableListOf<ColleagueItem>()
        var dialogProvider: (() -> AlertDialog)? = null

        class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
            val avatarText: TextView = itemView.findViewById(R.id.tvColleagueAvatarText)
            val avatarImage: ImageView = itemView.findViewById(R.id.ivColleagueAvatar)
            val name: TextView = itemView.findViewById(R.id.tvColleagueName)
            val subtitle: TextView = itemView.findViewById(R.id.tvColleagueSubtitle)
            val techBadge: ImageView = itemView.findViewById(R.id.ivColleagueTechBadge)
            val presenceDot: View = itemView.findViewById(R.id.vColleaguePresenceDot)
        }

        override fun onCreateViewHolder(parent: android.view.ViewGroup, viewType: Int): VH {
            val v = android.view.LayoutInflater.from(parent.context)
                .inflate(R.layout.item_colleague_search, parent, false)
            return VH(v)
        }

        override fun onBindViewHolder(holder: VH, position: Int) {
            val item = items[position]
            holder.name.text = item.fullName.ifBlank { item.login }
            holder.subtitle.text = buildString {
                append(item.login)
                if (item.employeeId.isNotBlank()) append(" • ${item.employeeId}")
                if (item.position.isNotBlank()) append(" • ${item.position}")
            }
            holder.techBadge.visibility = if (item.isTechAdmin) View.VISIBLE else View.GONE
            holder.presenceDot.setBackgroundResource(
                if (item.isOnline) R.drawable.bg_presence_online else R.drawable.bg_presence_offline
            )
            holder.avatarText.text = item.fullName.trim().firstOrNull()?.uppercase() ?: "?"
            val url = item.avatarUrl?.trim().orEmpty()
            if (url.isNotBlank()) {
                holder.avatarText.visibility = View.GONE
                holder.avatarImage.visibility = View.VISIBLE
                holder.avatarImage.load(url) {
                    crossfade(true)
                    error(R.drawable.ic_launcher_simple)
                }
            } else {
                holder.avatarImage.visibility = View.GONE
                holder.avatarText.visibility = View.VISIBLE
            }
            holder.itemView.setOnClickListener {
                val dialog = dialogProvider?.invoke() ?: return@setOnClickListener
                onClick(item, dialog)
            }
        }

        override fun getItemCount(): Int = items.size

        fun submit(newItems: List<ColleagueItem>) {
            items.clear()
            items.addAll(newItems)
            notifyDataSetChanged()
        }
    }
}

