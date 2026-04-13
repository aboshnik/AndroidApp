package com.example.app

import android.content.Intent
import android.util.TypedValue
import android.view.Gravity
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.DiffUtil
import androidx.recyclerview.widget.ListAdapter
import androidx.recyclerview.widget.RecyclerView
import com.example.app.api.PollItem
import com.example.app.api.PostItem
import coil.load
import coil.request.videoFrameMillis
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.Locale

class HomeFeedAdapter(
    private val canDeletePosts: Boolean,
    private val onDeletePost: (PostItem) -> Unit,
    private val onVote: (postId: Int, optionId: Int, onDone: (PollItem?) -> Unit) -> Unit
) : ListAdapter<PostItem, HomeFeedAdapter.VH>(Diff()) {
    private val mediaStartByPostId = mutableMapOf<Int, Int>()
    private data class VoteEntry(
        val login: String,
        val avatarUrl: String?,
        val optionText: String
    )

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        val v = LayoutInflater.from(parent.context).inflate(R.layout.item_home_post, parent, false)
        return VH(v, canDeletePosts, onDeletePost, onVote, mediaStartByPostId) { postId ->
            val idx = currentList.indexOfFirst { it.id == postId }
            if (idx >= 0) notifyItemChanged(idx)
        }
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        holder.bind(getItem(position))
    }

    class VH(
        itemView: View,
        private val canDeletePosts: Boolean,
        private val onDeletePost: (PostItem) -> Unit,
        private val onVote: (postId: Int, optionId: Int, onDone: (PollItem?) -> Unit) -> Unit,
        private val mediaStartByPostId: MutableMap<Int, Int>,
        private val refreshPost: (postId: Int) -> Unit
    ) : RecyclerView.ViewHolder(itemView) {
        private val tvTitle: TextView = itemView.findViewById(R.id.tvPostTitle)
        private val tvText: TextView = itemView.findViewById(R.id.tvPostText)
        private val tvMeta: TextView = itemView.findViewById(R.id.tvPostMeta)
        private val tvImportant: TextView = itemView.findViewById(R.id.tvPostImportant)
        private val mediaContainer: View = itemView.findViewById(R.id.postMediaContainer)
        private val mediaTop: ImageView = itemView.findViewById(R.id.postMediaTop)
        private val mediaBottomRow: LinearLayout = itemView.findViewById(R.id.postMediaBottomRow)
        private val mediaBottomLeft: ImageView = itemView.findViewById(R.id.postMediaBottomLeft)
        private val mediaBottomRight: ImageView = itemView.findViewById(R.id.postMediaBottomRight)
        private val mediaMore: TextView = itemView.findViewById(R.id.postMediaMoreOverlay)
        private val btnMediaPrev: TextView = itemView.findViewById(R.id.btnMediaPrev)
        private val btnMediaNext: TextView = itemView.findViewById(R.id.btnMediaNext)
        private val tvMediaIndex: TextView = itemView.findViewById(R.id.tvMediaIndex)
        private val tvPollBadge: TextView = itemView.findViewById(R.id.tvPollBadge)
        private val btnDeletePost: ImageView = itemView.findViewById(R.id.btnDeletePost)
        private val btnPostDetails: TextView = itemView.findViewById(R.id.btnPostDetails)
        private val btnPostPoll: TextView = itemView.findViewById(R.id.btnPostPoll)
        private val postCardContent: LinearLayout = itemView.findViewById(R.id.postCardContent)
        private val postCardHeader: LinearLayout = itemView.findViewById(R.id.postCardHeader)
        private val postCardBottomBlock: LinearLayout = itemView.findViewById(R.id.postCardBottomBlock)

        fun bind(item: PostItem) {
            val deleteReserveEnd =
                if (canDeletePosts) (44 * itemView.resources.displayMetrics.density).toInt() else 0
            postCardContent.setPaddingRelative(0, 0, 0, 0)
            postCardHeader.setPaddingRelative(0, 0, deleteReserveEnd, 0)
            postCardBottomBlock.setPaddingRelative(0, 0, deleteReserveEnd, 0)

            tvTitle.visibility = View.GONE
            tvText.text = item.content.trim().ifBlank { "Без текста" }
            tvMeta.text = formatDate(item.createdAt)
            tvImportant.visibility = if (item.isImportant) View.VISIBLE else View.GONE
            bindMedia(item)
            tvPollBadge.visibility = if (item.poll != null) View.VISIBLE else View.GONE

            val mediaUrls = collectMediaUrls(item)
            val hasLongText = item.content.trim().length > 180
            btnPostDetails.visibility =
                if (hasLongText || mediaUrls.isNotEmpty()) View.VISIBLE else View.GONE
            btnPostDetails.setOnClickListener { showPostDetailsDialog(item) }

            btnPostPoll.visibility = if (item.poll != null) View.VISIBLE else View.GONE
            btnPostPoll.setOnClickListener {
                item.poll?.let { poll -> showPollDialog(item, poll) }
            }

            btnDeletePost.visibility = if (canDeletePosts) View.VISIBLE else View.GONE
            btnDeletePost.setOnClickListener {
                AlertDialog.Builder(itemView.context)
                    .setTitle("Удалить новость?")
                    .setMessage("Действие нельзя отменить.")
                    .setPositiveButton("Удалить") { _, _ -> onDeletePost(item) }
                    .setNegativeButton(android.R.string.cancel, null)
                    .show()
            }
        }

        private fun collectMediaUrls(item: PostItem): List<String> =
            item.mediaUrls.orEmpty().ifEmpty { listOfNotNull(item.imageUrl) }
                .filter { it.isNotBlank() }

        private fun isVideoUrl(url: String): Boolean {
            val u = url.lowercase(Locale.getDefault())
            return u.contains(".mp4") || u.contains(".webm") || u.contains(".m3u8") ||
                u.contains("video/")
        }

        private fun showPostDetailsDialog(item: PostItem) {
            val ctx = itemView.context
            val view = LayoutInflater.from(ctx).inflate(R.layout.dialog_post_details, null, false)
            val mediaList = view.findViewById<LinearLayout>(R.id.postDetailsMediaList)
            val tvText = view.findViewById<TextView>(R.id.postDetailsText)

            mediaList.removeAllViews()
            val urls = collectMediaUrls(item)
            val density = ctx.resources.displayMetrics.density
            val imgH = (236 * density).toInt()
            val gap = (8 * density).toInt()

            urls.forEachIndexed { index, url ->
                val frame = FrameLayout(ctx).apply {
                    layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, imgH).apply {
                        topMargin = if (index == 0) 0 else gap
                    }
                }
                val iv = ImageView(ctx).apply {
                    layoutParams = FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT
                    )
                    scaleType = ImageView.ScaleType.CENTER_CROP
                    setBackgroundResource(R.drawable.bg_profile_action_row)
                    setOnClickListener { openMedia(url) }
                }
                iv.load(url) {
                    crossfade(true)
                    if (isVideoUrl(url)) videoFrameMillis(1000L)
                }
                frame.addView(iv)
                if (isVideoUrl(url)) {
                    val hint = TextView(ctx).apply {
                        layoutParams = FrameLayout.LayoutParams(
                            ViewGroup.LayoutParams.WRAP_CONTENT,
                            ViewGroup.LayoutParams.WRAP_CONTENT,
                            Gravity.BOTTOM or Gravity.END
                        ).apply { setMargins(gap, 0, gap, gap) }
                        text = "Видео · открыть"
                        setTextColor(0xFFFFFFFF.toInt())
                        setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f)
                        val p = (6 * density).toInt()
                        setPadding(p, p, p, p)
                        setBackgroundColor(0x66000000)
                    }
                    frame.addView(hint)
                }
                mediaList.addView(frame)
            }
            mediaList.visibility = if (urls.isEmpty()) View.GONE else View.VISIBLE

            tvText.text = item.content.trim().ifBlank { "Без текста" }

            AlertDialog.Builder(ctx)
                .setTitle("Подробнее")
                .setView(view)
                .setPositiveButton("Закрыть", null)
                .show()
        }

        private fun bindMedia(item: PostItem) {
            val media = collectMediaUrls(item)

            if (media.isEmpty()) {
                mediaContainer.visibility = View.GONE
                return
            }
            mediaContainer.visibility = View.VISIBLE
            val maxStart = (media.size - 1).coerceAtLeast(0)
            val start = mediaStartByPostId[item.id]?.coerceIn(0, maxStart) ?: 0
            mediaStartByPostId[item.id] = start

            mediaTop.load(media[start]) { crossfade(true) }
            mediaTop.setOnClickListener { openMedia(media[start]) }

            if (media.size == 1) {
                mediaBottomRow.visibility = View.GONE
                btnMediaPrev.visibility = View.GONE
                btnMediaNext.visibility = View.GONE
                tvMediaIndex.visibility = View.GONE
                return
            }
            mediaBottomRow.visibility = View.VISIBLE
            val second = media.getOrNull(start + 1)
            val third = media.getOrNull(start + 2)
            if (second != null) {
                mediaBottomLeft.visibility = View.VISIBLE
                mediaBottomLeft.load(second) { crossfade(true) }
                mediaBottomLeft.setOnClickListener { openMedia(second) }
            } else {
                mediaBottomLeft.visibility = View.INVISIBLE
                mediaBottomLeft.setOnClickListener(null)
            }
            if (third != null) {
                mediaBottomRight.visibility = View.VISIBLE
                mediaBottomRight.load(third) { crossfade(true) }
                mediaBottomRight.setOnClickListener { openMedia(third) }
            } else {
                mediaBottomRight.visibility = View.INVISIBLE
                mediaBottomRight.setOnClickListener(null)
            }

            val more = media.size - (start + 3)
            mediaMore.visibility = if (more > 0) View.VISIBLE else View.GONE
            mediaMore.text = if (more > 0) "+$more" else ""
            mediaMore.setOnClickListener { third?.let { openMedia(it) } }

            btnMediaPrev.visibility = if (start > 0) View.VISIBLE else View.GONE
            btnMediaNext.visibility = if (start < maxStart) View.VISIBLE else View.GONE
            tvMediaIndex.visibility = View.VISIBLE
            tvMediaIndex.text = "${start + 1}/${media.size}"
            btnMediaPrev.setOnClickListener {
                mediaStartByPostId[item.id] = (start - 1).coerceAtLeast(0)
                refreshPost(item.id)
            }
            btnMediaNext.setOnClickListener {
                mediaStartByPostId[item.id] = (start + 1).coerceAtMost(maxStart)
                refreshPost(item.id)
            }
        }

        private fun openMedia(url: String) {
            val i = Intent(itemView.context, MediaViewerActivity::class.java)
            i.putExtra(MediaViewerActivity.EXTRA_URL, url)
            itemView.context.startActivity(i)
        }

        private fun formatDate(rawUtc: String): String {
            val s = rawUtc.trim()
            if (s.isEmpty()) return ""
            val ru = Locale("ru", "RU")
            val here = ZoneId.systemDefault()
            val outFmt = DateTimeFormatter.ofPattern("dd.MM.yyyy, HH:mm", ru)
            val dateOnlyFmt = DateTimeFormatter.ofPattern("dd.MM.yyyy", ru)

            val zoned = runCatching {
                OffsetDateTime.parse(s).atZoneSameInstant(here)
            }.getOrNull()
                ?: runCatching {
                    val n = s.replace(' ', 'T')
                    LocalDateTime.parse(n, DateTimeFormatter.ISO_LOCAL_DATE_TIME)
                        .atZone(ZoneOffset.UTC)
                        .withZoneSameInstant(here)
                }.getOrNull()
                ?: runCatching {
                    if (s.length >= 10 && s[4] == '-' && s[7] == '-') {
                        LocalDate.parse(s.substring(0, 10)).atStartOfDay(here)
                    } else null
                }.getOrNull()

            return when {
                zoned != null -> zoned.format(outFmt)
                s.length >= 10 -> runCatching {
                    LocalDate.parse(s.substring(0, 10)).format(dateOnlyFmt)
                }.getOrElse { s }
                else -> s
            }
        }

        private fun showPollDialog(post: PostItem, poll: PollItem) {
            val ctx = itemView.context
            val root = LinearLayout(ctx).apply {
                orientation = LinearLayout.VERTICAL
                setPadding(28, 22, 28, 10)
            }
            val title = TextView(ctx).apply {
                text = poll.question
                textSize = 18f
                setTextColor(ctx.getColor(R.color.text_primary))
                setTypeface(null, android.graphics.Typeface.BOLD)
            }
            root.addView(title)

            if (!poll.description.isNullOrBlank()) {
                root.addView(TextView(ctx).apply {
                    text = poll.description
                    setTextColor(ctx.getColor(R.color.text_secondary))
                    textSize = 13f
                    setPadding(0, 6, 0, 10)
                })
            }

            val total = if (poll.totalVotes <= 0) 1 else poll.totalVotes
            val canVote = !poll.hasVoted || poll.allowRevote
            var pollDialog: AlertDialog? = null
            for (opt in poll.options) {
                val row = LinearLayout(ctx).apply {
                    orientation = LinearLayout.VERTICAL
                    setPadding(0, 8, 0, 8)
                }
                val pct = ((opt.votesCount * 100f) / total).toInt()
                val line1 = TextView(ctx).apply {
                    text = buildString {
                        append("$pct% ${opt.text}")
                        if (poll.selectedOptionId == opt.id) append("  ✓")
                    }
                    setTextColor(ctx.getColor(R.color.text_primary))
                    textSize = 15f
                }
                val progress = ProgressBar(ctx, null, android.R.attr.progressBarStyleHorizontal).apply {
                    max = 100
                    progress = pct
                }
                val line2 = TextView(ctx).apply {
                    text = "${opt.votesCount}"
                    setTextColor(ctx.getColor(R.color.text_secondary))
                    textSize = 12f
                    setPadding(0, 2, 0, 0)
                }
                row.addView(line1)
                row.addView(progress)
                row.addView(line2)
                if (canVote) {
                    row.setOnClickListener {
                        onVote(post.id, opt.id) { updatedPoll ->
                            val nextPoll = updatedPoll ?: return@onVote
                            pollDialog?.dismiss()
                            showPollDialog(post, nextPoll)
                        }
                    }
                }
                root.addView(row)
            }

            val footer = TextView(ctx).apply {
                text = "View Votes (${poll.totalVotes})"
                textSize = 14f
                setTextColor(ctx.getColor(R.color.button_primary))
                setPadding(0, 8, 0, 4)
                gravity = android.view.Gravity.CENTER_HORIZONTAL
            }
            footer.setOnClickListener {
                showVotesDialog(poll)
            }
            root.addView(footer)

            pollDialog = AlertDialog.Builder(ctx)
                .setView(root)
                .setNegativeButton("Закрыть", null)
                .show()
        }

        private fun showVotesDialog(poll: PollItem) {
            val ctx = itemView.context
            val hasVotes = poll.options.any { (it.voters?.isNotEmpty() == true) || it.votesCount > 0 }
            if (!poll.showVoters) {
                Toast.makeText(ctx, "Список голосов скрыт в настройках опроса", Toast.LENGTH_SHORT).show()
                return
            }
            if (!hasVotes) {
                Toast.makeText(ctx, "Пока нет голосов", Toast.LENGTH_SHORT).show()
                return
            }
            val entries = poll.options.flatMap { opt ->
                opt.voters.orEmpty()
                    .filter { it.login.isNotBlank() }
                    .map { voter ->
                        VoteEntry(
                            login = voter.login,
                            avatarUrl = voter.avatarUrl,
                            optionText = opt.text
                        )
                    }
            }.sortedWith(
                compareBy<VoteEntry> { it.optionText.lowercase(Locale("ru")) }
                    .thenBy { it.login.lowercase(Locale("ru")) }
            )
            if (entries.isEmpty()) {
                Toast.makeText(ctx, "Список голосов временно недоступен", Toast.LENGTH_SHORT).show()
                return
            }
            val view = LayoutInflater.from(ctx).inflate(R.layout.dialog_poll_votes, null, false)
            val recycler = view.findViewById<RecyclerView>(R.id.recyclerVotes)
            recycler.layoutManager = LinearLayoutManager(ctx)
            recycler.adapter = VoteEntriesAdapter(entries)
            AlertDialog.Builder(ctx)
                .setTitle("Кто проголосовал (${entries.size})")
                .setView(view)
                .setPositiveButton("OK", null)
                .show()
        }
    }

    private class VoteEntriesAdapter(
        private val items: List<VoteEntry>
    ) : RecyclerView.Adapter<VoteEntriesAdapter.VH>() {

        class VH(itemView: View) : RecyclerView.ViewHolder(itemView) {
            val avatarText: TextView = itemView.findViewById(R.id.tvVoteAvatarText)
            val avatarImage: ImageView = itemView.findViewById(R.id.ivVoteAvatar)
            val login: TextView = itemView.findViewById(R.id.tvVoteLogin)
            val option: TextView = itemView.findViewById(R.id.tvVoteOption)
        }

        override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
            val v = LayoutInflater.from(parent.context).inflate(R.layout.item_poll_vote, parent, false)
            return VH(v)
        }

        override fun onBindViewHolder(holder: VH, position: Int) {
            val item = items[position]
            holder.login.text = item.login
            holder.option.text = "Голос: ${item.optionText}"
            holder.avatarText.text = item.login.trim().take(1).uppercase(Locale("ru"))
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
        }

        override fun getItemCount(): Int = items.size
    }

    private class Diff : DiffUtil.ItemCallback<PostItem>() {
        override fun areItemsTheSame(oldItem: PostItem, newItem: PostItem) = oldItem.id == newItem.id
        override fun areContentsTheSame(oldItem: PostItem, newItem: PostItem) = oldItem == newItem
    }

    fun submit(items: List<PostItem>) = submitList(items)
}

