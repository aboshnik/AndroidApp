package com.example.app

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.view.View
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.ImageView
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.ApiClient
import com.example.app.api.PollItem
import com.example.app.api.PostItem
import com.example.app.api.EventRegisterRequest
import com.example.app.api.VoteRequest
import com.example.app.chats.ChatsActivity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class HomeActivity : BaseActivity() {
    override fun swipeTabIndex(): Int = 0
    private val autoRefreshIntervalMs = 5000L
    private val autoRefreshHandler = Handler(Looper.getMainLooper())
    private val autoRefreshRunnable = object : Runnable {
        override fun run() {
            loadFeed()
            autoRefreshHandler.postDelayed(this, autoRefreshIntervalMs)
        }
    }

    private lateinit var recycler: RecyclerView
    private lateinit var progress: ProgressBar
    private lateinit var empty: TextView
    private lateinit var adapter: HomeFeedAdapter

    private var employeeId: String = ""
    private var login: String = ""
    private var canDeletePosts: Boolean = false
    private val openCreatePost = registerForActivityResult(androidx.activity.result.contract.ActivityResultContracts.StartActivityForResult()) {
        if (it.resultCode == RESULT_OK) loadFeed()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!SessionManager.requireActive(this)) return
        setContentView(R.layout.activity_home)

        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        employeeId = auth.getString("employeeId", "")?.trim().orEmpty()
        login = auth.getString("login", "")?.trim().orEmpty()
        canDeletePosts = auth.getBoolean("canCreatePosts", false)

        recycler = findViewById(R.id.recyclerHomeFeed)
        progress = findViewById(R.id.homeProgress)
        empty = findViewById(R.id.homeEmpty)

        adapter = HomeFeedAdapter(
            canDeletePosts = canDeletePosts,
            onDeletePost = { post -> deletePost(post.id) },
            onVote = { postId, optionId, onDone -> vote(postId, optionId, onDone) },
            onRegisterEvent = { postId -> registerEvent(postId) },
            currentLogin = login
        )
        recycler.layoutManager = LinearLayoutManager(this)
        recycler.adapter = adapter

        findViewById<View>(R.id.btnCreateNews).setOnClickListener {
            openCreatePost.launch(Intent(this, CreatePostActivity::class.java))
            overridePendingTransition(R.anim.fade_in_fast, R.anim.fade_out_fast)
        }
        findViewById<View>(R.id.navHome).setOnClickListener { setBottomTab("home") }
        findViewById<View>(R.id.navChats).setOnClickListener {
            setBottomTab("chats")
            startActivity(Intent(this, ChatsActivity::class.java))
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
            startActivity(Intent(this, ShopActivity::class.java))
        }

        setBottomTab("home")
        loadFeed()
    }

    override fun onResume() {
        super.onResume()
        setBottomTab("home")
        MyFirebaseMessagingService.clearTrackedGeneralNotifications(this)
        startChatsUnreadBadgeAutoRefresh(employeeId = employeeId, badgeViewId = R.id.navChatsBadge)
        autoRefreshHandler.removeCallbacks(autoRefreshRunnable)
        autoRefreshHandler.postDelayed(autoRefreshRunnable, autoRefreshIntervalMs)
    }

    override fun onPause() {
        super.onPause()
        stopChatsUnreadBadgeAutoRefresh()
        autoRefreshHandler.removeCallbacks(autoRefreshRunnable)
    }

    private fun setBottomTab(tab: String) {
        val active = getColor(R.color.nav_active)
        val inactive = getColor(R.color.nav_inactive)
        fun setItem(containerId: Int, iconId: Int, textId: Int, activeTab: Boolean) {
            val container = findViewById<View>(containerId)
            findViewById<ImageView>(iconId).setColorFilter(if (activeTab) active else inactive)
            val tv = findViewById<TextView>(textId)
            tv.setTextColor(if (activeTab) active else inactive)
            tv.setTypeface(null, if (activeTab) android.graphics.Typeface.BOLD else android.graphics.Typeface.NORMAL)
            container.setBackgroundResource(
                if (activeTab) R.drawable.bg_bottom_nav_item_active
                else android.R.color.transparent
            )
        }
        setItem(R.id.navHome, R.id.navHomeIcon, R.id.navHomeText, tab == "home")
        setItem(R.id.navChats, R.id.navChatsIcon, R.id.navChatsText, tab == "chats")
        setItem(R.id.navSettings, R.id.navSettingsIcon, R.id.navSettingsText, tab == "settings")
        setItem(R.id.navProfile, R.id.navProfileIcon, R.id.navProfileText, tab == "profile")
        setItem(R.id.navContacts, R.id.navContactsIcon, R.id.navContactsText, tab == "store")
    }

    private fun loadFeed() {
        progress.visibility = View.VISIBLE
        empty.visibility = View.GONE

        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.postApi.getFeed(login = login.ifBlank { null })
                }
                progress.visibility = View.GONE
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    empty.text = body?.message ?: getString(R.string.error_network)
                    empty.visibility = View.VISIBLE
                    adapter.submit(emptyList())
                    return@launch
                }
                val list: List<PostItem> = body.posts.orEmpty()
                adapter.submit(list)
                empty.visibility = if (list.isEmpty()) View.VISIBLE else View.GONE
                if (list.isEmpty()) empty.text = "Пока нет новостей"
            } catch (e: Exception) {
                progress.visibility = View.GONE
                empty.text = "${getString(R.string.error_network)} ${e.message}"
                empty.visibility = View.VISIBLE
                adapter.submit(emptyList())
            }
        }
    }

    private fun deletePost(postId: Int) {
        if (login.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.postApi.deletePost(id = postId, login = login)
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast("Новость удалена")
                loadFeed()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun vote(postId: Int, optionId: Int, onDone: (PollItem?) -> Unit) {
        if (login.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.postApi.vote(id = postId, request = VoteRequest(login = login, optionId = optionId))
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                onDone(body.poll)
                loadFeed()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun registerEvent(postId: Int) {
        if (login.isBlank()) return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.postApi.registerEvent(postId, EventRegisterRequest(login))
                }
                val body = resp.body()
                if (!resp.isSuccessful || body == null || !body.success) {
                    safeToast(body?.message ?: getString(R.string.error_network), long = true)
                    return@launch
                }
                safeToast(body.message.ifBlank { "Регистрация выполнена" })
                loadFeed()
            } catch (e: Exception) {
                safeToast("${getString(R.string.error_network)} ${e.message}", long = true)
            }
        }
    }

    private fun showContactsDialog() {
        val view = layoutInflater.inflate(R.layout.dialog_contacts, null, false)
        safeShowDialog(
            androidx.appcompat.app.AlertDialog.Builder(this)
                .setView(view)
                .setPositiveButton("Закрыть", null)
        )
    }
}

