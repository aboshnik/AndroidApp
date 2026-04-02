package com.example.app

import android.content.Intent
import android.os.Bundle
import android.util.Log
import android.view.View
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.ApiClient
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class HomeActivity : AppCompatActivity() {

    private val scope = CoroutineScope(Dispatchers.Main + Job())
    private lateinit var recyclerFeed: RecyclerView
    private lateinit var feedProgress: ProgressBar
    private lateinit var feedEmpty: TextView
    private lateinit var notifBadge: TextView
    private lateinit var adapter: FeedAdapter
    private val logTag = "HomeActivityNet"
    private var canUseDevConsole: Boolean = false
    private var currentLogin: String = ""
    private var currentEmployeeId: String = ""
    private var currentLastName: String = ""
    private var currentFirstName: String = ""
    private var currentPhone: String = ""
    private var currentCanCreatePosts: Boolean = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_home)

        val auth = getSharedPreferences("auth", MODE_PRIVATE)
        currentLogin = (intent.getStringExtra("login") ?: auth.getString("login", "")).orEmpty().trim()
        currentEmployeeId = (intent.getStringExtra("employeeId") ?: auth.getString("employeeId", "")).orEmpty()
        currentLastName = (intent.getStringExtra("lastName") ?: auth.getString("lastName", "")).orEmpty()
        currentFirstName = (intent.getStringExtra("firstName") ?: auth.getString("firstName", "")).orEmpty()
        currentPhone = (intent.getStringExtra("phone") ?: auth.getString("phone", "")).orEmpty()
        currentCanCreatePosts = intent.getBooleanExtra("canCreatePosts", auth.getBoolean("canCreatePosts", false))
        canUseDevConsole = intent.getBooleanExtra("canUseDevConsole", auth.getBoolean("canUseDevConsole", false))

        // Ensure background notifications can work (login stored for Worker)
        currentLogin.takeIf { it.isNotBlank() }?.let { login ->
            auth.edit()
                .putString("login", login)
                .putString("employeeId", currentEmployeeId)
                .putString("lastName", currentLastName)
                .putString("firstName", currentFirstName)
                .putString("phone", currentPhone)
                .putBoolean("canCreatePosts", currentCanCreatePosts)
                .putBoolean("canUseDevConsole", canUseDevConsole)
                .apply()
            NotificationsWorker.schedule(this)
        }

        recyclerFeed = findViewById(R.id.recyclerFeed)
        feedProgress = findViewById(R.id.feedProgress)
        feedEmpty = findViewById(R.id.feedEmpty)
        notifBadge = findViewById(R.id.tvNotifBadge)

        recyclerFeed.layoutManager = LinearLayoutManager(this)

        findViewById<View>(R.id.navHome).setOnClickListener { }
        findViewById<View>(R.id.navCalendar).setOnClickListener {
            val i = Intent(this, CalendarActivity::class.java)
            i.putExtra("employeeId", currentEmployeeId)
            i.putExtra("login", currentLogin)
            i.putExtra("lastName", currentLastName)
            i.putExtra("firstName", currentFirstName)
            i.putExtra("phone", currentPhone)
            i.putExtra("canCreatePosts", currentCanCreatePosts)
            i.putExtra("canUseDevConsole", canUseDevConsole)
            startActivity(i)
        }
        findViewById<View>(R.id.navProfile).setOnClickListener { openProfile() }

        val canCreatePosts = currentCanCreatePosts
        adapter = FeedAdapter(
            canManagePosts = canCreatePosts,
            onDeletePost = { post -> deletePost(post.id) }
        )
        recyclerFeed.adapter = adapter

        val addWrap = findViewById<View>(R.id.btnAddWrap)
        addWrap.visibility = if (canCreatePosts) View.VISIBLE else View.GONE
        addWrap.setOnClickListener { openCreatePost() }
        findViewById<View>(R.id.btnNotifications).setOnClickListener {
            openNotifications()
        }
    }

    override fun onResume() {
        super.onResume()
        if (!SessionManager.requireActive(this)) return
        loadFeed()
        loadNotificationsBadge()

        // Re-schedule in case app was restored without a fresh login
        val login = getSharedPreferences("auth", MODE_PRIVATE).getString("login", "")?.trim().orEmpty()
        if (login.isNotBlank()) {
            NotificationsWorker.schedule(this)
        }
    }

    private fun openNotifications() {
        val login = currentLogin
        if (login.isBlank()) {
            Toast.makeText(this, getString(R.string.error_network), Toast.LENGTH_SHORT).show()
            return
        }
        val i = Intent(this, NotificationsActivity::class.java)
        i.putExtra("login", login)
        startActivity(i)
    }

    private fun openCreatePost() {
        val canCreatePosts = currentCanCreatePosts
        if (!canCreatePosts) {
            Toast.makeText(this, getString(R.string.no_permission_create_posts), Toast.LENGTH_SHORT).show()
            return
        }
        val login = currentLogin
        if (login.isNullOrBlank()) {
            Toast.makeText(this, getString(R.string.error_network), Toast.LENGTH_SHORT).show()
            return
        }
        val i = Intent(this, CreatePostActivity::class.java)
        i.putExtra("login", login)
        startActivity(i)
    }

    private fun loadNotificationsBadge() {
        val login = currentLogin
        if (login.isBlank()) return

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.notificationsApi.getNotifications(login = login, take = 20)
                }
                val body = response.body()
                if (!response.isSuccessful || body == null || !body.success) {
                    notifBadge.visibility = View.GONE
                    return@launch
                }
                val unread = body.unreadCount
                if (unread > 0) {
                    notifBadge.text = if (unread > 9) "9+" else unread.toString()
                    notifBadge.visibility = View.VISIBLE
                } else {
                    notifBadge.visibility = View.GONE
                }
            } catch (_: Exception) {
                notifBadge.visibility = View.GONE
            }
        }
    }

    private fun loadFeed() {
        feedProgress.visibility = View.VISIBLE
        feedEmpty.visibility = View.GONE
        recyclerFeed.visibility = View.VISIBLE

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.postApi.getFeed()
                }
                feedProgress.visibility = View.GONE
                val body = response.body()
                if (!response.isSuccessful || body == null) {
                    feedEmpty.text = body?.message ?: getString(R.string.error_network)
                    feedEmpty.visibility = View.VISIBLE
                    return@launch
                }
                val posts = body.posts ?: emptyList()
                adapter.submitList(posts)
                if (posts.isEmpty()) {
                    feedEmpty.visibility = View.VISIBLE
                } else {
                    feedEmpty.visibility = View.GONE
                }
            } catch (e: Exception) {
                feedProgress.visibility = View.GONE
                val details = buildErrorDetails(e)
                Log.e(logTag, details, e)
                feedEmpty.text = "${getString(R.string.error_network)}\n$details"
                feedEmpty.visibility = View.VISIBLE
                showNetworkErrorDialog(details)
            }
        }
    }

    private fun deletePost(postId: Int) {
        val login = currentLogin.trim()
        if (login.isBlank()) {
            Toast.makeText(this, getString(R.string.error_network), Toast.LENGTH_SHORT).show()
            return
        }

        scope.launch {
            try {
                val response = withContext(Dispatchers.IO) {
                    ApiClient.postApi.deletePost(id = postId, login = login)
                }
                val body = response.body()
                if (!response.isSuccessful || body == null || !body.success) {
                    Toast.makeText(
                        this@HomeActivity,
                        body?.message ?: getString(R.string.error_network),
                        Toast.LENGTH_LONG
                    ).show()
                    return@launch
                }
                Toast.makeText(this@HomeActivity, body.message, Toast.LENGTH_SHORT).show()
                loadFeed()
            } catch (e: Exception) {
                val details = buildErrorDetails(e)
                showNetworkErrorDialog(details)
            }
        }
    }

    private fun buildErrorDetails(e: Throwable): String {
        var t: Throwable? = e
        val chain = mutableListOf<String>()
        var guard = 0
        while (t != null && guard < 5) {
            val name = t.javaClass.simpleName
            val msg = t.message?.trim().orEmpty()
            chain += if (msg.isNotBlank()) "$name: $msg" else name
            t = t.cause
            guard++
        }
        return chain.joinToString(" -> ")
    }

    private fun showNetworkErrorDialog(details: String) {
        AlertDialog.Builder(this)
            .setTitle("Подробности ошибки сети")
            .setMessage(details)
            .setPositiveButton("OK", null)
            .show()
    }

    private fun openProfile() {
        val i = Intent(this, ProfileActivity::class.java)
        i.putExtra("employeeId", currentEmployeeId)
        i.putExtra("login", currentLogin)
        i.putExtra("lastName", currentLastName)
        i.putExtra("firstName", currentFirstName)
        i.putExtra("phone", currentPhone)
        i.putExtra("canCreatePosts", currentCanCreatePosts)
        i.putExtra("canUseDevConsole", canUseDevConsole)
        startActivity(i)
        finish()
    }
}
