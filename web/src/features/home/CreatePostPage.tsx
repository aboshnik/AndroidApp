import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { createPost, createPostWithMedia } from '../../api/post'
import type { PollCreateRequest } from '../../api/types'
import { getSession } from '../../shared/session'

export function CreatePostPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
  const session = getSession()
  const [content, setContent] = useState('')
  const [isImportant, setIsImportant] = useState(false)
  const [pollEnabled, setPollEnabled] = useState(false)
  const [pollQuestion, setPollQuestion] = useState('')
  const [pollOptions, setPollOptions] = useState<string[]>(['', ''])
  const [files, setFiles] = useState<File[]>([])
  const [eventEnabled, setEventEnabled] = useState(false)

  if (!session) return null

  const submit = useMutation({
    mutationFn: async () => {
      const poll: PollCreateRequest | null = pollEnabled
        ? {
            question: pollQuestion.trim(),
            options: pollOptions.map((v) => v.trim()).filter(Boolean),
            allowRevote: true,
            showVoters: false,
            shuffleOptions: false,
            allowMediaInQuestionAndOptions: false,
            hideResultsUntilEnd: false,
            creatorCanViewWithoutVoting: true,
          }
        : null
      const isEvent = eventEnabled

      if (files.length > 0) {
        const resp = await createPostWithMedia({
          content: content.trim(),
          authorLogin: session.login,
          isImportant,
          poll,
          isEvent,
          files,
        })
        if (!resp.success) throw new Error(resp.message || 'Не удалось создать новость')
        return
      }
      const resp = await createPost({
        content: content.trim(),
        authorLogin: session.login,
        isImportant,
        poll,
        isEvent,
      })
      if (!resp.success) throw new Error(resp.message || 'Не удалось создать новость')
    },
    onSuccess: () => navigate(`${basePath}/home`),
  })

  return (
    <section className="card create-post-page">
      <h2>Новая новость</h2>
      <label>
        Текст новости
        <textarea value={content} onChange={(e) => setContent(e.target.value)} rows={5} maxLength={3333} />
      </label>
      <label className="remember-row">
        <input type="checkbox" checked={isImportant} onChange={(e) => setIsImportant(e.target.checked)} />
        <span>Важная новость</span>
      </label>
      <label>
        Медиа (фото/видео)
        <input
          type="file"
          multiple
          accept="image/*,video/*"
          onChange={(e) => setFiles(Array.from(e.target.files ?? []))}
        />
      </label>
      {files.length > 0 ? <p className="muted">Выбрано файлов: {files.length}</p> : null}
      <label className="remember-row">
        <input type="checkbox" checked={pollEnabled} onChange={(e) => setPollEnabled(e.target.checked)} />
        <span>Добавить опрос</span>
      </label>
      {pollEnabled ? (
        <div className="poll-editor">
          <label>
            Вопрос
            <input value={pollQuestion} onChange={(e) => setPollQuestion(e.target.value)} />
          </label>
          {pollOptions.map((value, idx) => (
            <label key={idx}>
              Вариант {idx + 1}
              <input
                value={value}
                onChange={(e) =>
                  setPollOptions((prev) => prev.map((it, i) => (i === idx ? e.target.value : it)))
                }
              />
            </label>
          ))}
          <button type="button" onClick={() => setPollOptions((prev) => [...prev, ''])}>
            + Вариант
          </button>
        </div>
      ) : null}

      <hr />
      <label className="remember-row">
        <input
          type="checkbox"
          checked={eventEnabled}
          onChange={(e) => setEventEnabled(e.target.checked)}
        />
        <span>Мероприятие</span>
      </label>
      {submit.error ? <p className="error">{(submit.error as Error).message}</p> : null}
      <div className="post-actions">
        <button type="button" onClick={() => navigate(`${basePath}/home`)}>
          Отмена
        </button>
        <button
          type="button"
          disabled={
            submit.isPending ||
            !content.trim() ||
            (pollEnabled && (!pollQuestion.trim() || pollOptions.filter((x) => x.trim()).length < 2))
          }
          onClick={() => submit.mutate()}
        >
          {submit.isPending ? 'Публикация...' : 'Опубликовать'}
        </button>
      </div>
    </section>
  )
}
