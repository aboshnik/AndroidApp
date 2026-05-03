import { useMutation, useQuery } from '@tanstack/react-query'
import { addToCart, checkoutCart, getCoinsBalance, getCoinsShop } from '../../api/coins'
import { getSession } from '../../shared/session'

export function ShopPage() {
  const session = getSession()
  const login = session?.login ?? ''
  const balanceQuery = useQuery({
    queryKey: ['coins-balance', login],
    queryFn: () => getCoinsBalance(login),
    enabled: !!login,
    refetchInterval: 5000,
  })
  const shopQuery = useQuery({
    queryKey: ['coins-shop'],
    queryFn: getCoinsShop,
  })
  const buyMutation = useMutation({
    mutationFn: async (productId: number) => {
      const addResp = await addToCart({ login, productId, quantity: 1 })
      if (!addResp.success) throw new Error(addResp.message || 'Не удалось добавить в корзину')
      const checkoutResp = await checkoutCart({ login })
      if (!checkoutResp.success) throw new Error(checkoutResp.message || 'Не удалось оформить покупку')
      return checkoutResp
    },
    onSuccess: () => {
      window.alert('Покупка успешно оформлена')
      void balanceQuery.refetch()
      void shopQuery.refetch()
    },
    onError: (e) => {
      window.alert(e instanceof Error ? e.message : 'Ошибка покупки')
    },
  })

  const balance = balanceQuery.data?.balance?.balance ?? 0
  return (
    <section className="iphone-profile-page">
      <h2>Магазин</h2>
      <div className="iphone-group">
        <div className="iphone-info-card">
          <strong>Ваш баланс: {balance}</strong>
          <p className="muted">Следующая выдача через {balanceQuery.data?.balance?.nextPayoutDays ?? 7} дней</p>
        </div>
      </div>
      {shopQuery.error ? <p className="error">{(shopQuery.error as Error).message}</p> : null}
      <div className="iphone-group">
        {(shopQuery.data?.items ?? []).map((item) => (
          <div key={item.id} className="iphone-info-card">
            <strong>{item.title}</strong>
            <p className="muted">
              {item.category} · {item.price} коинов
              {typeof item.stock === 'number' ? ` · В наличии: ${item.stock}` : ''}
            </p>
            <button
              type="button"
              disabled={buyMutation.isPending || !item.id || (typeof item.stock === 'number' && item.stock <= 0)}
              onClick={() => {
                const productId = Number(item.id)
                if (!Number.isFinite(productId) || productId <= 0) {
                  window.alert('Некорректный товар')
                  return
                }
                buyMutation.mutate(productId)
              }}
            >
              {buyMutation.isPending ? 'Покупка...' : 'Купить'}
            </button>
          </div>
        ))}
        {(shopQuery.data?.items ?? []).length === 0 ? (
          <div className="iphone-info-card">
            <strong>Раздел в разработке</strong>
            <p className="muted">Здесь позже настроим товары и обмен коинов.</p>
          </div>
        ) : null}
      </div>
    </section>
  )
}

